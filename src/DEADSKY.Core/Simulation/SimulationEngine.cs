using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Weapons;

namespace DEADSKY.Core.Simulation;

public sealed class SimulationEngine : IDisposable
{
    private static readonly double[] SupportedRadarRangesNm = [40, 80, 120];
    private readonly System.Timers.Timer _timer;
    private readonly object _tickLock = new();
    private bool _running;
    private int _tickCount;
    private DateTime? _pausedAtUtc;

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
        if (_pausedAtUtc.HasValue)
        {
            var pausedDuration = DateTime.UtcNow - _pausedAtUtc.Value;
            Radar.TrackManager.ShiftWallClock(pausedDuration);
            _pausedAtUtc = null;
        }

        _running = true;
        _timer.Start();
    }

    public void Pause()
    {
        _running = false;
        _timer.Stop();
        _pausedAtUtc ??= DateTime.UtcNow;
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
        _pausedAtUtc = DateTime.UtcNow;
        ResetWorld(scenario.PlayerBattery);
        ApplyWeather(scenario.Weather);

        if (scenario.PlayerBattery is { } batteryCfg)
        {
            SetAlertLevel(ParseAlertLevel(batteryCfg.InitialAlert));
            SetROE(ParseRoe(batteryCfg.InitialROE), scenario.Command.Callsign);
        }

        BuildSnapshot();
    }

    public void RefreshSnapshot()
    {
        lock (_tickLock)
        {
            BuildSnapshot();
        }
    }

    public int FlushPendingComms(int maxMessages = 32)
    {
        lock (_tickLock)
        {
            int processed = Comms.ProcessQueue(maxMessages);
            BuildSnapshot();
            return processed;
        }
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

    public bool PlayerToggleTrackHold(string trackId)
    {
        var battery = Entities.GetPlayerBattery();
        var track = Radar.TrackManager.GetById(trackId);
        if (track == null)
            return false;

        bool newHeldState = !track.IsTrackHeld || track.IsDesignated;
        if (!Radar.TrackManager.HoldTrack(trackId, newHeldState))
            return false;

        if (newHeldState && battery != null && battery.RadarMode == RadarMode.Search)
        {
            battery.RadarMode = RadarMode.TrackWhileScan;
            battery.RadarOnline = true;
            Radar.SetMode(RadarMode.TrackWhileScan);
        }

        BuildSnapshot();
        return true;
    }

    public bool PlayerReleaseTrack(string trackId)
    {
        var battery = Entities.GetPlayerBattery();
        var track = Radar.TrackManager.GetById(trackId);
        if (track == null)
            return false;

        bool wasHardLock = track.IsDesignated ||
            (!string.IsNullOrWhiteSpace(track.EntityId) && battery?.DesignatedTargetId == track.EntityId);

        if (!Radar.TrackManager.ReleaseTrack(trackId))
            return false;

        if (battery != null && !string.IsNullOrWhiteSpace(track.EntityId) && battery.DesignatedTargetId == track.EntityId)
            battery.DesignatedTargetId = null;

        if (battery != null && wasHardLock && battery.RadarMode == RadarMode.SingleTargetTrack)
        {
            battery.RadarMode = RadarMode.TrackWhileScan;
            battery.RadarOnline = true;
            Radar.SetMode(RadarMode.TrackWhileScan);
        }
        else if (battery != null)
        {
            Radar.SetMode(battery.RadarMode, battery.DesignatedTargetId);
        }

        BuildSnapshot();
        return true;
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

        if (mode != RadarMode.SingleTargetTrack && !string.IsNullOrWhiteSpace(battery.DesignatedTargetId))
        {
            var hardLockTrack = Radar.TrackManager.GetByEntityId(battery.DesignatedTargetId);
            if (hardLockTrack != null)
            {
                hardLockTrack.IsDesignated = false;
                if (mode == RadarMode.TrackWhileScan)
                    hardLockTrack.IsTrackHeld = true;
            }

            battery.DesignatedTargetId = null;
        }

        battery.RadarMode = mode;
        battery.RadarOnline = mode is not RadarMode.Silent and not RadarMode.Standby;
        Radar.SetMode(mode, battery.DesignatedTargetId);
        BuildSnapshot();
    }

    public void PlayerSetRadarRange(double rangeNm)
    {
        var battery = Entities.GetPlayerBattery();
        if (battery == null)
            return;

        rangeNm = NormalizeRadarRange(rangeNm);
        battery.RadarRangeNm = rangeNm;
        Radar.SetRange(rangeNm);
        BuildSnapshot();
    }

    public static double NormalizeRadarRange(double rangeNm)
    {
        if (double.IsNaN(rangeNm) || double.IsInfinity(rangeNm))
            return SupportedRadarRangesNm[1];

        double best = SupportedRadarRangesNm[0];
        double bestDistance = Math.Abs(rangeNm - best);
        foreach (double candidate in SupportedRadarRangesNm)
        {
            double distance = Math.Abs(rangeNm - candidate);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    public RadioMessage PlayerSendMessage(RadioChannel channel, string content, string? recipient = null) =>
        Comms.SendPlayerMessage(channel, content, recipient);

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
            Weapons.Update(deltaTime);
            Entities.UpdateAll(deltaTime);
            Radar.Update(deltaTime, Entities.GetActiveSnapshot(), Weather.PrecipitationMmHr, battery);
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

    private void ApplyWeather(WeatherConfig? config)
    {
        Weather.VisibilityNm = config?.VisibilityNm ?? 80;
        Weather.CloudCeilingFt = config?.CloudCeilingFt ?? 25000;
        Weather.PrecipitationMmHr = config?.PrecipitationMmHr ?? 0;
        Weather.Description = config?.Description ?? "Clear";
        Weather.WindSpeedKts = config?.WindSpeedKts ?? 8;
        Weather.WindDirectionDeg = config?.WindDirectionDeg ?? 180;
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
        var trackSnapshot = Radar.TrackManager.GetAllTracks()
            .Select(CloneTrack)
            .ToList();

        LatestSnapshot = new SimulationSnapshot
        {
            GameTimeSec = GameTimeSec,
            GameTimeString = TimeSpan.FromSeconds(GameTimeSec).ToString(@"hh\:mm\:ss") + " ZULU",
            Battery = battery == null ? null : CloneBattery(battery),
            AllTracks = trackSnapshot,
            FirmTracks = trackSnapshot.Where(track => track.Quality != TrackQuality.Lost).ToList(),
            HostileTracks = trackSnapshot.Where(track =>
                track.Classification is TrackClassification.Hostile or TrackClassification.AssumedHostile).ToList(),
            HostileAircraft = Entities.GetHostileAircraft().OfType<Aircraft>().Select(CloneAircraft).ToList(),
            ActiveMissiles = Entities.GetActiveMissiles().Select(CloneMissile).ToList(),
            ActiveEcmEffects = Radar.ActiveEcmEffects.ToList(),
            RadarSweepAngle = Radar.SweepAngleDeg,
            RadarRangeNm = battery?.RadarRangeNm ?? Radar.Model.MaxRangeNm,
            RadarMode = battery?.RadarMode ?? RadarMode.Search,
            Weather = Weather.Clone()
        };
    }

    private static SAMBattery CloneBattery(SAMBattery battery)
    {
        var clone = new SAMBattery
        {
            Callsign = battery.Callsign,
            Position = battery.Position,
            VelocityX = battery.VelocityX,
            VelocityY = battery.VelocityY,
            HeadingDeg = battery.HeadingDeg,
            AltitudeM = battery.AltitudeM,
            SpeedMps = battery.SpeedMps,
            RequestedHeadingDeg = battery.RequestedHeadingDeg,
            RequestedAltitudeM = battery.RequestedAltitudeM,
            RequestedSpeedMps = battery.RequestedSpeedMps,
            RadarMode = battery.RadarMode,
            RadarRangeNm = battery.RadarRangeNm,
            RadarMinAltFt = battery.RadarMinAltFt,
            RadarOnline = battery.RadarOnline,
            HasLowAltitudeModule = battery.HasLowAltitudeModule,
            HasECCMSuite = battery.HasECCMSuite,
            RadarSweepAngle = battery.RadarSweepAngle,
            RadarSweepRateDegSec = battery.RadarSweepRateDegSec,
            MaxLaunchers = battery.MaxLaunchers,
            ReserveMissiles = battery.ReserveMissiles,
            CurrentMissileType = battery.CurrentMissileType,
            MissilesFired = battery.MissilesFired,
            ConfirmedKills = battery.ConfirmedKills,
            Misses = battery.Misses,
            AlertLevel = battery.AlertLevel,
            ROE = battery.ROE,
            DesignatedTargetId = battery.DesignatedTargetId,
            PowerOnline = battery.PowerOnline,
            CommsOnline = battery.CommsOnline,
            CoolingNominal = battery.CoolingNominal,
            RadarHealthPct = battery.RadarHealthPct,
            IsUnderAttack = battery.IsUnderAttack,
            HasDataLink = battery.HasDataLink,
            HasPassiveDetection = battery.HasPassiveDetection,
            HasMobileCapability = battery.HasMobileCapability,
            HasDecoyEmitter = battery.HasDecoyEmitter,
            HasBackupPower = battery.HasBackupPower,
            HasHardenedComms = battery.HasHardenedComms,
            MissileMaxRangeNm = battery.MissileMaxRangeNm,
            MissileMinRangeNm = battery.MissileMinRangeNm,
            MissileMaxAltFt = battery.MissileMaxAltFt,
            MissileMinAltFt = battery.MissileMinAltFt,
            MissileSingleShotPk = battery.MissileSingleShotPk,
            ECMActive = battery.ECMActive,
            DamagePct = battery.DamagePct,
            SpawnTime = battery.SpawnTime,
            Status = battery.Status
        };

        clone.Launchers.Clear();
        foreach (var launcher in battery.Launchers)
        {
            clone.Launchers.Add(new Launcher
            {
                Id = launcher.Id,
                State = launcher.State,
                MissileType = launcher.MissileType,
                ReloadTimeSec = launcher.ReloadTimeSec,
                ReloadProgress = launcher.ReloadProgress,
                ActiveMissileId = launcher.ActiveMissileId,
                HasRapidReload = launcher.HasRapidReload,
                HasAutoLoader = launcher.HasAutoLoader
            });
        }

        clone.SyncPhysicsState();
        return clone;
    }

    private static TrackFile CloneTrack(TrackFile track)
    {
        var clone = new TrackFile
        {
            TrackId = track.TrackId,
            EntityId = track.EntityId,
            TrackDesignation = track.TrackDesignation,
            GroupLabel = track.GroupLabel,
            Classification = track.Classification,
            ClassificationConfidence = track.ClassificationConfidence,
            Position = track.Position,
            AltitudeM = track.AltitudeM,
            HeadingDeg = track.HeadingDeg,
            SpeedMps = track.SpeedMps,
            Velocity = track.Velocity,
            PositionUncertaintyM = track.PositionUncertaintyM,
            Quality = track.Quality,
            DetectionCount = track.DetectionCount,
            LastDetectionTime = track.LastDetectionTime,
            TrackInitiatedTime = track.TrackInitiatedTime,
            IFFInterrogated = track.IFFInterrogated,
            IFFResponse = track.IFFResponse,
            IFFTime = track.IFFTime,
            IsDesignated = track.IsDesignated,
            IsTrackHeld = track.IsTrackHeld,
            IsBeingEngaged = track.IsBeingEngaged,
            AssignedMissileId = track.AssignedMissileId,
            ThreatLevel = track.ThreatLevel,
            TimeToThreatSec = track.TimeToThreatSec,
            ClosingSpeedMps = track.ClosingSpeedMps
        };

        foreach (var point in track.History)
            clone.History.Add(point);

        return clone;
    }

    private static Aircraft CloneAircraft(Aircraft aircraft)
    {
        var clone = new Aircraft
        {
            Designation = aircraft.Designation,
            Type = aircraft.Type,
            Affiliation = aircraft.Affiliation,
            Role = aircraft.Role,
            FuelCapacityKg = aircraft.FuelCapacityKg,
            FuelBurnRateKgSec = aircraft.FuelBurnRateKgSec,
            BingoFuelKg = aircraft.BingoFuelKg,
            HasECM = aircraft.HasECM,
            HasARMCapability = aircraft.HasARMCapability,
            EcmPower = aircraft.EcmPower,
            AggressivenessLevel = aircraft.AggressivenessLevel,
            CurrentBehavior = aircraft.CurrentBehavior,
            FuelRemainingKg = aircraft.FuelRemainingKg,
            TargetEntityId = aircraft.TargetEntityId,
            TargetWaypoint = aircraft.TargetWaypoint,
            Waypoints = aircraft.Waypoints.ToList(),
            CurrentWaypointIndex = aircraft.CurrentWaypointIndex,
            GroupId = aircraft.GroupId,
            FormationLeaderId = aircraft.FormationLeaderId,
            FormationSlot = aircraft.FormationSlot,
            RadarLockDetected = aircraft.RadarLockDetected,
            RadarLockBearingDeg = aircraft.RadarLockBearingDeg,
            RadarLockDetectedTime = aircraft.RadarLockDetectedTime,
            MissileInbound = aircraft.MissileInbound,
            MissileInboundDetectedTime = aircraft.MissileInboundDetectedTime,
            Position = aircraft.Position,
            VelocityX = aircraft.VelocityX,
            VelocityY = aircraft.VelocityY,
            HeadingDeg = aircraft.HeadingDeg,
            AltitudeM = aircraft.AltitudeM,
            SpeedMps = aircraft.SpeedMps,
            RequestedHeadingDeg = aircraft.RequestedHeadingDeg,
            RequestedAltitudeM = aircraft.RequestedAltitudeM,
            RequestedSpeedMps = aircraft.RequestedSpeedMps,
            RcsM2 = aircraft.RcsM2,
            FlightModel = aircraft.FlightModel,
            IsBeingEngaged = aircraft.IsBeingEngaged,
            EngagedByMissileId = aircraft.EngagedByMissileId,
            ECMActive = aircraft.ECMActive,
            DamagePct = aircraft.DamagePct,
            SpawnTime = aircraft.SpawnTime,
            Status = aircraft.Status,
            CallSign = aircraft.CallSign
        };

        clone.SyncPhysicsState();
        return clone;
    }

    private static SAMMissile CloneMissile(SAMMissile missile)
    {
        var clone = new SAMMissile
        {
            MissileTypeName = missile.MissileTypeName,
            Guidance = missile.Guidance,
            MaxFlightTimeSec = missile.MaxFlightTimeSec,
            WarheadRadiusM = missile.WarheadRadiusM,
            FuzeRadiusM = missile.FuzeRadiusM,
            SingleShotPk = missile.SingleShotPk,
            GuidanceMemorySec = missile.GuidanceMemorySec,
            TargetEntityId = missile.TargetEntityId,
            Phase = missile.Phase,
            FlightTimeSec = missile.FlightTimeSec,
            GuidanceActive = missile.GuidanceActive,
            LaunchedByBatteryId = missile.LaunchedByBatteryId,
            LauncherId = missile.LauncherId,
            Position = missile.Position,
            VelocityX = missile.VelocityX,
            VelocityY = missile.VelocityY,
            HeadingDeg = missile.HeadingDeg,
            AltitudeM = missile.AltitudeM,
            SpeedMps = missile.SpeedMps,
            RequestedHeadingDeg = missile.RequestedHeadingDeg,
            RequestedAltitudeM = missile.RequestedAltitudeM,
            RequestedSpeedMps = missile.RequestedSpeedMps,
            RcsM2 = missile.RcsM2,
            FlightModel = missile.FlightModel,
            IsBeingEngaged = missile.IsBeingEngaged,
            EngagedByMissileId = missile.EngagedByMissileId,
            ECMActive = missile.ECMActive,
            DamagePct = missile.DamagePct,
            SpawnTime = missile.SpawnTime,
            Status = missile.Status,
            CallSign = missile.CallSign
        };

        clone.SyncPhysicsState();
        return clone;
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
