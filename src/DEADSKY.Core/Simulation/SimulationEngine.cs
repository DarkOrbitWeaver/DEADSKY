using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Weapons;

namespace DEADSKY.Core.Simulation;

public sealed class SimulationEngine : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly object _tickLock = new();
    private bool _running;
    private int _tickCount;

    public EntityManager Entities { get; } = new();
    public RadarSystem Radar { get; } = new();
    public CommManager Comms { get; } = new();
    public EventBus Events { get; } = new();
    public WeaponsSystem Weapons { get; }
    public WeatherState Weather { get; } = new();
    public double GameTimeSec { get; private set; }
    public SimulationSnapshot LatestSnapshot { get; private set; } = new();
    public Action<SimulationSnapshot>? OnTickForAI { get; set; }

    public SimulationEngine()
    {
        Weapons = new WeaponsSystem(Entities, Radar.TrackManager);

        _timer = new System.Timers.Timer(100);
        _timer.AutoReset = true;
        _timer.Elapsed += (_, _) => Tick(0.1);

        WireEvents();
        ResetWorld();
    }

    public void Start()
    {
        _running = true;
        _timer.Start();
    }

    public void Pause()
    {
        _running = false;
        _timer.Stop();
    }

    public void Dispose()
    {
        Pause();
        _timer.Dispose();
    }

    public void LoadScenario(ScenarioDefinition scenario)
    {
        Pause();
        GameTimeSec = 0;
        _tickCount = 0;
        ResetWorld(scenario.PlayerBattery);

        if (scenario.PlayerBattery is { } batteryCfg)
        {
            SetAlertLevel(ParseAlertLevel(batteryCfg.InitialAlert));
            SetROE(ParseRoe(batteryCfg.InitialROE), scenario.Command.Callsign);
        }

        BuildSnapshot();
    }

    public bool PlayerDesignate(string trackId)
    {
        bool ok = Weapons.DesignateTarget(trackId);
        if (ok)
        {
            var battery = Entities.GetPlayerBattery();
            var track = Radar.TrackManager.GetById(trackId);
            if (battery != null && track?.EntityId != null)
            {
                battery.RadarMode = RadarMode.SingleTargetTrack;
                Radar.SetMode(RadarMode.SingleTargetTrack, track.EntityId);
            }
        }
        return ok;
    }

    public bool PlayerFire(string trackId)
    {
        var battery = Entities.GetPlayerBattery();
        if (battery == null)
            return false;

        PlayerDesignate(trackId);
        return Weapons.FireAtDesignated(battery, trackId) == EngagementResult.MissileInFlight;
    }

    public List<EngagementResult> PlayerSalvo(string trackId, int count)
    {
        var battery = Entities.GetPlayerBattery();
        if (battery == null)
            return new List<EngagementResult>();

        PlayerDesignate(trackId);
        return Weapons.FireSalvo(battery, trackId, count);
    }

    public void PlayerSetRadarMode(RadarMode mode)
    {
        var battery = Entities.GetPlayerBattery();
        if (battery == null)
            return;

        battery.RadarMode = mode;
        Radar.SetMode(mode);
    }

    public void PlayerSetRadarRange(double rangeNm)
    {
        var battery = Entities.GetPlayerBattery();
        if (battery == null)
            return;

        battery.RadarRangeNm = rangeNm;
        Radar.SetRange(rangeNm);
    }

    public RadioMessage PlayerSendMessage(RadioChannel channel, string content) =>
        Comms.SendPlayerMessage(channel, content);

    public void SetAlertLevel(BatteryAlertLevel level)
    {
        var battery = Entities.GetPlayerBattery();
        if (battery == null || battery.AlertLevel == level)
            return;

        var old = battery.AlertLevel;
        battery.AlertLevel = level;
        Events.Publish(new AlertLevelChangedEvent(old.ToString(), level.ToString()));
    }

    public void SetROE(RulesOfEngagement roe, string authority)
    {
        var battery = Entities.GetPlayerBattery();
        if (battery == null || battery.ROE == roe)
            return;

        var old = battery.ROE;
        battery.ROE = roe;
        Events.Publish(new ROEChangedEvent(old.ToString(), roe.ToString(), authority));
    }

    private void Tick(double deltaTime)
    {
        if (!_running)
            return;

        lock (_tickLock)
        {
            GameTimeSec += deltaTime;

            var battery = Entities.GetPlayerBattery();
            Entities.UpdateAll(deltaTime);
            Radar.Update(deltaTime, Entities.GetActiveSnapshot(), Weather.PrecipitationMmHr, battery);
            Weapons.Update(deltaTime);
            Comms.ProcessQueue();

            BuildSnapshot();

            _tickCount++;
            Events.Publish(new SimulationTickEvent(GameTimeSec, deltaTime, _tickCount));
            OnTickForAI?.Invoke(LatestSnapshot);
        }
    }

    private void ResetWorld(PlayerBatteryConfig? config = null)
    {
        Entities.Clear();
        Radar.TrackManager.Clear();

        var battery = CreateBatteryFromConfig(config);
        Entities.Add(battery);
        Radar.SetRange(battery.RadarRangeNm);
        Radar.SetMode(battery.RadarMode);
        BuildSnapshot();
    }

    private SAMBattery CreateBatteryFromConfig(PlayerBatteryConfig? config)
    {
        var battery = new SAMBattery
        {
            Callsign = config?.Callsign ?? "ALPHA",
            ReserveMissiles = config?.ReserveMissiles ?? 12,
            RadarRangeNm = config?.RadarRangeNm ?? 80,
            CurrentMissileType = config?.MissileType ?? "9M38",
            MissileMaxRangeNm = config?.EngagementRangeNm ?? 18,
            MissileSingleShotPk = config?.MissilePk ?? 0.7,
            RadarMode = RadarMode.Search
        };

        int launcherCount = config?.Launchers ?? 4;
        battery.Launchers.Clear();
        for (int i = 1; i <= launcherCount; i++)
        {
            battery.Launchers.Add(new Launcher
            {
                Id = $"L{i}",
                State = LauncherState.Ready,
                MissileType = battery.CurrentMissileType
            });
        }

        battery.SyncPhysicsState();
        return battery;
    }

    private void BuildSnapshot()
    {
        var battery = Entities.GetPlayerBattery();
        LatestSnapshot = new SimulationSnapshot
        {
            GameTimeSec = GameTimeSec,
            GameTimeString = TimeSpan.FromSeconds(GameTimeSec).ToString(@"hh\:mm\:ss") + " ZULU",
            Battery = battery,
            AllTracks = Radar.TrackManager.GetAllTracks().ToList(),
            FirmTracks = Radar.TrackManager.GetFirmTracks().ToList(),
            HostileTracks = Radar.TrackManager.GetHostileTracks().ToList(),
            HostileAircraft = Entities.GetHostileAircraft().OfType<Aircraft>().ToList(),
            ActiveMissiles = Entities.GetActiveMissiles().ToList(),
            ActiveEcmEffects = Radar.ActiveEcmEffects.ToList(),
            RadarSweepAngle = Radar.SweepAngleDeg,
            RadarRangeNm = battery?.RadarRangeNm ?? Radar.Model.MaxRangeNm,
            RadarMode = battery?.RadarMode ?? RadarMode.Search,
            Weather = Weather.Clone()
        };
    }

    private void WireEvents()
    {
        Radar.TrackManager.TrackInitiated += track =>
            Events.Publish(new NewContactEvent(track.TrackId, track.Classification.ToString()));

        Radar.TrackManager.TrackDropped += track =>
            Events.Publish(new TrackDroppedEvent(track.TrackId));

        Weapons.MissileLaunched += (missile, target) =>
        {
            var trackId = Radar.TrackManager.GetByEntityId(target.Id)?.TrackId ?? target.Id;
            Events.Publish(new MissileLaunchedEvent(missile.Id, trackId, missile.LauncherId));
        };

        Weapons.EngagementCompleted += (_, target, result) =>
        {
            string trackId = target == null
                ? "UNKNOWN"
                : Radar.TrackManager.GetByEntityId(target.Id)?.TrackId ?? target.Id;
            bool wasKill = result == EngagementResult.KillConfirmed;
            Events.Publish(new EngagementResultEvent(trackId, result.ToString(), wasKill));
        };
    }

    private static BatteryAlertLevel ParseAlertLevel(string text) => text.ToLowerInvariant() switch
    {
        "green" => BatteryAlertLevel.Green,
        "orange" => BatteryAlertLevel.Orange,
        "red" => BatteryAlertLevel.Red,
        "black" => BatteryAlertLevel.Black,
        _ => BatteryAlertLevel.Yellow
    };

    private static RulesOfEngagement ParseRoe(string text) => text.ToLowerInvariant() switch
    {
        "weapons_free" => RulesOfEngagement.WeaponsFree,
        "weapons_hold" => RulesOfEngagement.WeaponsHold,
        _ => RulesOfEngagement.WeaponsTight
    };
}
