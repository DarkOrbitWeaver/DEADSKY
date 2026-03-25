using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DEADSKY.AI.Agents;
using DEADSKY.AI.Client;
using DEADSKY.AI.Tools;
using DEADSKY.Audio;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Economy;
using DEADSKY.Core.Entities;
using DEADSKY.Core.EnemyAI;
using DEADSKY.Core.Events;
using DEADSKY.Core.Personnel;
using DEADSKY.Core.Progression;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;
using DEADSKY.Core.Weapons;

namespace DEADSKY.App.ViewModels;

/// <summary>
/// Main ViewModel — the single source of truth for the entire UI.
/// Owns the SimulationEngine, all game systems, and updates WPF bindings.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    // ── Core systems ──────────────────────────────────────────────────
    public SimulationEngine Sim { get; } = new();
    public AudioEngine Audio { get; } = new();
    public ScenarioManager Scenario { get; }
    public CrewRoster Crew { get; } = CrewRoster.CreateDefaultCrew();
    public BudgetSystem Budget { get; } = new();
    public PlayerProfile Profile { get; } = new();
    public EventEngine Events { get; }
    public AgentOrchestrator? AI { get; private set; }
    private GroupTacticManager _tactics = null!;

    // ── Observable state ──────────────────────────────────────────────
    [ObservableProperty] private SimulationSnapshot? _currentSnapshot;
    [ObservableProperty] private string _gameTime = "00:00:00 ZULU";
    [ObservableProperty] private string _alertLevelText = "YELLOW";
    [ObservableProperty] private string _roeText = "WEAPONS TIGHT";
    [ObservableProperty] private string _missionName = "AWAITING BRIEFING";
    [ObservableProperty] private bool _simulationRunning;
    [ObservableProperty] private string? _selectedTrackId;
    [ObservableProperty] private TrackFile? _selectedTrack;
    [ObservableProperty] private string _statusMessage = "SYSTEM STANDBY";
    [ObservableProperty] private bool _aiAvailable;
    [ObservableProperty] private RadioChannel _activeChannel = RadioChannel.CommandNet;
    [ObservableProperty] private string _inputMessage = "";
    [ObservableProperty] private string _selectedTrackBraa = "BRAA ---";
    [ObservableProperty] private string _selectedTrackThreatText = "NO TARGET SELECTED";
    [ObservableProperty] private string _selectedTrackEnvelopeText = "ENGAGEMENT WINDOW: STANDBY";
    [ObservableProperty] private string _radarCursorReadout = "CURSOR BRAA: ---";
    [ObservableProperty] private string _weatherSummary = "WX CLR | VIS 80NM | CEIL 25000FT";
    [ObservableProperty] private int _commandUnreadCount;
    [ObservableProperty] private int _batteryUnreadCount;
    [ObservableProperty] private int _intelUnreadCount;

    // Battery state
    [ObservableProperty] private int _readyLaunchers = 4;
    [ObservableProperty] private int _reserveMissiles = 12;
    [ObservableProperty] private int _confirmedKills;
    [ObservableProperty] private int _trackCount;
    [ObservableProperty] private int _hostileCount;
    [ObservableProperty] private string _batteryROE = "TIGHT";
    [ObservableProperty] private string _radarModeText = "SEARCH";

    // Alert level color (bound to header)
    [ObservableProperty] private string _alertColor = "#FFC800";
    [ObservableProperty] private string _aiStatusColor = "#FFC800";

    // ── Message collections ────────────────────────────────────────────
    public ObservableCollection<RadioMessage> CommandNetMessages { get; } = new();
    public ObservableCollection<RadioMessage> BatteryNetMessages { get; } = new();
    public ObservableCollection<RadioMessage> IntelNetMessages { get; } = new();
    public ObservableCollection<RadioMessage> AllMessagesRecent { get; } = new();

    // ── Launcher states ────────────────────────────────────────────────
    public ObservableCollection<LauncherViewModel> LauncherStates { get; } = new();

    // ── Threat board ───────────────────────────────────────────────────
    public ObservableCollection<TrackRowViewModel> ThreatBoardTracks { get; } = new();

    // ── Crew display ──────────────────────────────────────────────────
    public ObservableCollection<SoldierViewModel> CrewDisplay { get; } = new();
    public string AiModelText => AIModelClient.ModelIdentifier;
    public string AiEndpointText => AIModelClient.ModelsEndpoint;
    public string AiStatusText => AiAvailable ? "ONLINE" : "OFFLINE";
    public string CommandChannelLabel => CommandUnreadCount > 0 ? $"CMD {CommandUnreadCount}" : "CMD";
    public string BatteryChannelLabel => BatteryUnreadCount > 0 ? $"BAT {BatteryUnreadCount}" : "BAT";
    public string IntelChannelLabel => IntelUnreadCount > 0 ? $"INT {IntelUnreadCount}" : "INT";
    public string ThreatSummaryText => HostileCount == 0
        ? "AIR PICTURE CLEAN"
        : $"{HostileCount} HOSTILE / {TrackCount} TRACKS";

    // ── Constructor ───────────────────────────────────────────────────

    public MainViewModel()
    {
        Scenario = new ScenarioManager(Sim);
        Events = new EventEngine(Sim, Crew);
        _tactics = new GroupTacticManager(Sim.Entities);

        InitializeSystems();
        WireSimEvents();
        InitAI();
        PopulateCrewDisplay();
    }

    private void InitializeSystems()
    {
        Audio.Initialize();

        // Initialize 4 launcher VMs
        for (int i = 1; i <= 4; i++)
            LauncherStates.Add(new LauncherViewModel { LauncherId = $"L{i}" });
    }

    private void WireSimEvents()
    {
        // Simulation tick → update UI (called from sim thread, must dispatch)
        Sim.Events.Subscribe<SimulationTickEvent>(OnSimTick);
        Sim.Events.Subscribe<NewContactEvent>(OnNewContact);
        Sim.Events.Subscribe<EngagementResultEvent>(OnEngagementResult);
        Sim.Events.Subscribe<MissileLaunchedEvent>(OnMissileLaunched);
        Sim.Events.Subscribe<AlertLevelChangedEvent>(OnAlertChanged);
        Sim.Events.Subscribe<ROEChangedEvent>(OnROEChanged);
        Sim.Events.Subscribe<MissionCompletedEvent>(OnMissionCompleted);
        Sim.Events.Subscribe<BatteryDamagedEvent>(OnBatteryDamaged);

        Sim.Comms.MessageReceived += OnMessageReceived;

        Crew.CrewEventOccurred += OnCrewEvent;
        Events.EventFired += OnGameEventFired;

        Scenario.OnWaveSpawned += wave => SetStatus($"NEW THREAT: {wave}");
        Scenario.OnMissionComplete += (outcome, reason) =>
            DispatchToUI(() => OnMissionEnd(outcome, reason));
    }

    private async void InitAI()
    {
        var client = new AIModelClient();
        var toolRegistry = new ToolRegistry(Sim, _tactics);
        var commanderProfile = EnemyCommanderProfile.ForChapter(1);

        AI = new AgentOrchestrator(client, toolRegistry, commanderProfile, _tactics);

        bool available = await AI.CheckAvailabilityAsync();
        AiAvailable = available;
        AiStatusColor = available ? "#00DC00" : "#FFC800";
        RefreshAiBindings();
        SetStatus(available
            ? $"AI ONLINE — {AIModelClient.ModelIdentifier}"
            : "AI OFFLINE — running without AI");

        // Hook AI into sim tick
        Sim.OnTickForAI = snapshot => AI.Tick(0.05, snapshot);

        // Subscribe to new contacts for intel agent
        Sim.Events.Subscribe<NewContactEvent>(async evt =>
        {
            if (AI?.AIAvailable == true)
                await AI.HandleNewContactAsync(evt.TrackId, Sim.LatestSnapshot);
        });
    }

    private void PopulateCrewDisplay()
    {
        foreach (var s in Crew.Soldiers)
            CrewDisplay.Add(new SoldierViewModel(s));
    }

    // ── Simulation control commands ───────────────────────────────────

    [RelayCommand]
    public async Task RefreshAiStatus()
    {
        if (AI == null)
            return;

        AiAvailable = await AI.CheckAvailabilityAsync();
        AiStatusColor = AiAvailable ? "#00DC00" : "#FFC800";
        RefreshAiBindings();
        SetStatus(AiAvailable
            ? $"AI LINK CONFIRMED — {AIModelClient.ModelIdentifier}"
            : "AI LINK LOST — CHECK LM STUDIO SERVER");
    }

    [RelayCommand]
    public void StartMission()
    {
        if (SimulationRunning) return;
        Sim.Start();
        SimulationRunning = true;
        SetStatus("MISSION ACTIVE");
        Audio.Play(SoundEvent.SystemOnline);
        RefreshCommandStates();
    }

    [RelayCommand]
    public void PauseMission()
    {
        if (!SimulationRunning) return;
        Sim.Pause();
        SimulationRunning = false;
        SetStatus("MISSION PAUSED");
        RefreshCommandStates();
    }

    [RelayCommand]
    public void LoadScenario(string scenarioPath)
    {
        try
        {
            var loader = new ScenarioLoader();
            var scenarioKey = scenarioPath?.Trim().ToLowerInvariant();
            ScenarioDefinition scenario = scenarioKey switch
            {
                null or "" or "tutorial" => CreateTutorialScenario(),
                "operation" or "deadsky" or "strike" => CreateOperationDeadskyScenario(),
                _ when File.Exists(scenarioPath) => loader.Load(scenarioPath),
                _ => CreateTutorialScenario()
            };

            ResetUiForScenarioLoad();
            Scenario.LoadScenario(scenario);
            MissionName = scenario.Name;
            WeatherSummary = BuildWeatherSummary(Sim.LatestSnapshot);
            RadarCursorReadout = "CURSOR BRAA: ---";
            SimulationRunning = false;
            RefreshDerivedBindings();
            RefreshCommandStates();
            SetStatus($"SCENARIO LOADED: {scenario.Name}");
        }
        catch (Exception ex)
        {
            SetStatus($"SCENARIO LOAD ERROR: {ex.Message}");
        }
    }

    // ── Engagement commands ───────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanDesignate))]
    public void DesignateSelected()
    {
        if (SelectedTrackId == null) return;
        bool ok = Sim.PlayerDesignate(SelectedTrackId);
        if (ok)
        {
            SetStatus($"DESIGNATED: {SelectedTrackId}");
            Audio.Play(SoundEvent.LockOnWarning);
        }
    }

    private bool CanDesignate() => SelectedTrackId != null && SimulationRunning;

    [RelayCommand(CanExecute = nameof(CanFire))]
    public void FireSingle()
    {
        if (SelectedTrackId == null) return;
        bool fired = Sim.PlayerFire(SelectedTrackId);
        if (fired)
        {
            Audio.Play(SoundEvent.MissileLaunch);
            SetStatus($"MISSILE AWAY — {SelectedTrackId}");
        }
        else
            SetStatus($"FIRE DENIED — {Sim.Weapons.LastError}");
    }

    private bool CanFire() => SelectedTrackId != null && SimulationRunning &&
                              (Sim.Entities.GetPlayerBattery()?.ReadyLaunchers ?? 0) > 0;

    [RelayCommand(CanExecute = nameof(CanFire))]
    public void FireSalvo()
    {
        if (SelectedTrackId == null) return;
        var battery = Sim.Entities.GetPlayerBattery();
        if (battery == null) return;
        var results = Sim.PlayerSalvo(SelectedTrackId, Math.Min(2, battery.ReadyLaunchers));
        int fired = results.Count(r => r == EngagementResult.MissileInFlight);
        if (fired > 0)
        {
            Audio.Play(SoundEvent.MissileLaunch);
            SetStatus($"SALVO ×{fired} AWAY — {SelectedTrackId}");
        }
    }

    // ── Radar commands ────────────────────────────────────────────────

    [RelayCommand]
    public void SetRadarSearch()
    {
        Sim.PlayerSetRadarMode(RadarMode.Search);
        SetStatus("RADAR MODE SEARCH");
        RefreshDerivedBindings();
    }

    [RelayCommand]
    public void SetRadarTWS()
    {
        Sim.PlayerSetRadarMode(RadarMode.TrackWhileScan);
        SetStatus("RADAR MODE TWS");
        RefreshDerivedBindings();
    }

    [RelayCommand]
    public void SetRadarSilent()
    {
        Sim.PlayerSetRadarMode(RadarMode.Silent);
        SetStatus("RADAR MODE SILENT");
        RefreshDerivedBindings();
    }

    [RelayCommand]
    public void SetRadarRange(double rangeNm)
    {
        Sim.PlayerSetRadarRange(rangeNm);
        SetStatus($"RADAR RANGE SET {rangeNm:F0} NM");
        RefreshDerivedBindings();
    }

    [RelayCommand]
    public void SetRadarRangePreset(string rangeNmText)
    {
        if (double.TryParse(rangeNmText, out var rangeNm))
            SetRadarRange(rangeNm);
    }

    // ── Comms commands ────────────────────────────────────────────────

    [RelayCommand]
    public async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(InputMessage)) return;
        await SendPlayerRadioAsync(ActiveChannel, InputMessage.Trim());
        InputMessage = "";
    }

    [RelayCommand]
    public async Task SendQuickCommand(string commandKey)
    {
        var route = BuildQuickCommand(commandKey);
        if (route.Message.Length == 0)
        {
            SetStatus("QUICK COMMAND UNAVAILABLE FOR CURRENT CONTEXT");
            return;
        }

        await SendPlayerRadioAsync(route.Channel, route.Message);
    }

    [RelayCommand]
    public void SelectChannel(RadioChannel channel)
    {
        ActiveChannel = channel;
        Sim.Comms.MarkAllRead(channel);
        RefreshUnreadCounts();
        Audio.Play(SoundEvent.ChannelSwitch);
    }

    // ── Track selection ───────────────────────────────────────────────

    public void SelectTrack(string trackId)
    {
        SelectedTrackId = trackId;
        SelectedTrack = Sim.Radar.TrackManager.GetById(trackId);
        RefreshSelectedTrackReadout();
        RefreshCommandStates();
    }

    public void SelectRadarPoint(double bearingDeg, double rangeNm)
    {
        RadarCursorReadout = $"CURSOR BRAA {bearingDeg:000}/{rangeNm:0.0}";
        SetStatus($"CURSOR BRAA {bearingDeg:000}/{rangeNm:0.0}");
    }

    // ── Simulation event handlers (called on sim thread) ─────────────

    private void OnSimTick(SimulationTickEvent evt)
    {
        var snapshot = Sim.LatestSnapshot;
        DispatchToUI(() => UpdateFromSnapshot(snapshot));
        Scenario.Update(evt.GameTimeSec);
        Events.Update(evt.DeltaTime);
        Crew.Update(evt.DeltaTime,
            underFire: snapshot.HostileTracks.Any(t => t.RangeNm < 25),
            recentKill: false, recentLoss: false);
    }

    private void UpdateFromSnapshot(SimulationSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        GameTime = snapshot.GameTimeString;
        WeatherSummary = BuildWeatherSummary(snapshot);

        var battery = snapshot.Battery;
        if (battery != null)
        {
            ReadyLaunchers = battery.ReadyLaunchers;
            ReserveMissiles = battery.ReserveMissiles;
            ConfirmedKills = battery.ConfirmedKills;
            AlertLevelText = battery.AlertLevel.ToString().ToUpper();
            RoeText = battery.ROE.ToString().Replace("Weapons", "WEAPONS ").ToUpper();
            RadarModeText = battery.RadarMode.ToString().ToUpper();
            AlertColor = battery.AlertLevel switch
            {
                BatteryAlertLevel.Green => "#00B400",
                BatteryAlertLevel.Yellow => "#FFC800",
                BatteryAlertLevel.Orange => "#FF8200",
                BatteryAlertLevel.Red => "#FF0000",
                BatteryAlertLevel.Black => "#640000",
                _ => "#FFC800"
            };

            // Update launcher VMs
            for (int i = 0; i < LauncherStates.Count && i < battery.Launchers.Count; i++)
                LauncherStates[i].Update(battery.Launchers[i]);
        }

        TrackCount = snapshot.AllTracks.Count;
        HostileCount = snapshot.HostileTracks.Count;

        // Update threat board
        UpdateThreatBoard(snapshot);

        // Update selected track
        if (SelectedTrackId != null)
            SelectedTrack = snapshot.AllTracks.FirstOrDefault(t => t.TrackId == SelectedTrackId);

        RefreshSelectedTrackReadout();
        RefreshCommandStates();
        RefreshDerivedBindings();
    }

    private void UpdateThreatBoard(SimulationSnapshot snapshot)
    {
        var hostile = snapshot.AllTracks
            .Where(t => t.Classification is TrackClassification.Hostile or
                        TrackClassification.AssumedHostile or TrackClassification.Unknown)
            .OrderByDescending(t => t.ThreatLevel)
            .ToList();

        // Sync collection
        for (int i = 0; i < hostile.Count; i++)
        {
            if (i < ThreatBoardTracks.Count)
                ThreatBoardTracks[i].Update(hostile[i]);
            else
                ThreatBoardTracks.Add(new TrackRowViewModel(hostile[i]));
        }
        while (ThreatBoardTracks.Count > hostile.Count)
            ThreatBoardTracks.RemoveAt(ThreatBoardTracks.Count - 1);

        if (hostile.Count == 0)
        {
            SelectedTrackId = null;
            SelectedTrack = null;
            RefreshSelectedTrackReadout();
            return;
        }

        if (SelectedTrackId == null || hostile.All(t => t.TrackId != SelectedTrackId))
            SelectTrack(hostile[0].TrackId);
    }

    private void OnNewContact(NewContactEvent evt)
    {
        DispatchToUI(() =>
        {
            Audio.Play(SoundEvent.NewContact);
            SetStatus($"NEW CONTACT: {evt.TrackId} [{evt.Classification}]");
        });
    }

    private void OnEngagementResult(EngagementResultEvent evt)
    {
        DispatchToUI(() =>
        {
            if (evt.WasKill)
            {
                Audio.Play(SoundEvent.MissileImpact);
                SetStatus($"SPLASH — {evt.TrackId} DESTROYED");
                Crew.ApplyMoraleBoost(0.05, "Kill confirmed");
            }
            else
            {
                Audio.Play(SoundEvent.MissileMiss);
                SetStatus($"MISS — {evt.TrackId}");
            }
        });
    }

    private void OnMissileLaunched(MissileLaunchedEvent evt)
    {
        DispatchToUI(() => SetStatus($"MISSILE AWAY → {evt.TargetTrackId} [{evt.LauncherId}]"));
    }

    private void OnAlertChanged(AlertLevelChangedEvent evt)
    {
        DispatchToUI(() =>
        {
            if (evt.NewLevel is "Red" or "Black")
                Audio.Play(SoundEvent.AlertKlaxon);
            SetStatus($"ALERT LEVEL: {evt.NewLevel.ToUpper()}");
        });
    }

    private void OnROEChanged(ROEChangedEvent evt)
    {
        DispatchToUI(() =>
        {
            Audio.Play(SoundEvent.FlashMessageAlert);
            SetStatus($"ROE CHANGE: {evt.NewROE.Replace("Weapons", "WEAPONS ")}");
        });
    }

    private void OnMissionCompleted(MissionCompletedEvent evt)
    {
        DispatchToUI(() =>
        {
            string outcome = evt.Victory ? "VICTORY" : "DEFEAT";
            SetStatus($"MISSION {outcome}: {evt.Reason}");
            Sim.Pause();
            SimulationRunning = false;
            RefreshCommandStates();
        });
    }

    private void OnBatteryDamaged(BatteryDamagedEvent evt)
    {
        DispatchToUI(() =>
        {
            Audio.Play(SoundEvent.AlertKlaxon);
            SetStatus($"BATTERY HIT: {evt.ComponentDamaged}");
            Crew.ApplyFearEvent(0.3, "battery hit");
        });
    }

    private void OnMessageReceived(RadioMessage msg)
    {
        DispatchToUI(() =>
        {
            var target = msg.Channel switch
            {
                RadioChannel.CommandNet => CommandNetMessages,
                RadioChannel.BatteryNet => BatteryNetMessages,
                RadioChannel.IntelNet => IntelNetMessages,
                _ => null
            };
            target?.Add(msg);
            msg.IsRead = msg.Channel == ActiveChannel;
            AllMessagesRecent.Add(msg);
            if (AllMessagesRecent.Count > 100)
                AllMessagesRecent.RemoveAt(0);
            RefreshUnreadCounts();

            if (msg.Priority == MessagePriority.Flash)
                Audio.Play(SoundEvent.FlashMessageAlert);
            else if (!msg.IsFromPlayer)
                Audio.Play(SoundEvent.RadioSquelchOpen);
        });
    }

    private void OnCrewEvent(Soldier soldier, string eventType)
    {
        DispatchToUI(() =>
        {
            var vm = CrewDisplay.FirstOrDefault(s => s.SoldierId == soldier.Id);
            vm?.Refresh();
        });
    }

    private void OnGameEventFired(GameEvent evt)
    {
        DispatchToUI(() =>
        {
            if (evt.Category is GameEventCategory.RadarMalfunction)
                Audio.Play(SoundEvent.SystemOffline);
        });
    }

    private void OnMissionEnd(ScenarioManager.MissionOutcome outcome, string reason)
    {
        string outcomeStr = outcome == ScenarioManager.MissionOutcome.Victory ? "VICTORY" :
                            outcome == ScenarioManager.MissionOutcome.PartialVictory ? "PARTIAL VICTORY" : "DEFEAT";
        SetStatus($"{outcomeStr}: {reason}");
        SimulationRunning = false;
        RefreshCommandStates();

        // Award budget
        var battery = Sim.Entities.GetPlayerBattery();
        if (battery != null)
        {
            var reward = MissionRewardCalculator.Calculate(
                5000, battery.ConfirmedKills, battery.MissilesFired,
                Crew.Soldiers.All(s => s.Health == HealthStatus.Healthy),
                Scenario.EnemyBreakthroughs == 0,
                battery.HitRate, Profile.MissionsCompleted + 1);
            Budget.Earn(reward.TotalReward, "Mission completion");
            Profile.MissionsCompleted++;
            Profile.TotalKills += battery.ConfirmedKills;
            Profile.EvaluateRankPromotion();
        }
    }

    // ── Utilities ─────────────────────────────────────────────────────

    private void SetStatus(string msg) => StatusMessage = msg;

    private async Task SendPlayerRadioAsync(RadioChannel channel, string content)
    {
        ActiveChannel = channel;
        var msg = Sim.PlayerSendMessage(channel, content);
        msg.IsRead = true;
        Sim.Comms.MarkAllRead(channel);
        RefreshUnreadCounts();
        Audio.Play(SoundEvent.RadioSquelchOpen);
        SetStatus($"{channel.ToString().ToUpper()}: {content}");

        if (AI != null)
            await AI.HandlePlayerMessageAsync(channel, msg.Content, Sim.LatestSnapshot);
    }

    private (RadioChannel Channel, string Message) BuildQuickCommand(string commandKey) => commandKey switch
    {
        "ack" => (RadioChannel.CommandNet, "ECHO, ALPHA. ROGER. TRACKING CURRENT PICTURE."),
        "picture" => (RadioChannel.CommandNet, "ECHO, ALPHA. REQUEST PICTURE."),
        "status" => (RadioChannel.CommandNet,
            $"ECHO, ALPHA. SITREP. TRACKS {TrackCount}, HOSTILES {HostileCount}, READY {ReadyLaunchers}, RESERVE {ReserveMissiles}."),
        "declare" when SelectedTrack != null => (RadioChannel.CommandNet,
            $"ECHO, ALPHA. REQUEST DECLARE {SelectedTrackId}. {BuildTrackBraa(SelectedTrack)}."),
        "weapons_free" => (RadioChannel.CommandNet,
            $"ECHO, ALPHA. REQUEST WEAPONS FREE. {(SelectedTrackId ?? "HOSTILE GROUP")} CLOSING."),
        "intel" => (RadioChannel.IntelNet,
            SelectedTrack != null
                ? $"INTEL, ALPHA. ASSESS {SelectedTrackId}. {BuildTrackBraa(SelectedTrack)}."
                : "INTEL, ALPHA. ASSESS HIGHEST THREAT CONTACT."),
        _ => (ActiveChannel, string.Empty)
    };

    private void RefreshSelectedTrackReadout()
    {
        SelectedTrackBraa = SelectedTrack != null ? BuildTrackBraa(SelectedTrack) : "BRAA ---";
        SelectedTrackThreatText = SelectedTrack != null
            ? $"{SelectedTrack.Classification.ToString().ToUpper()} | {SelectedTrack.AspectString.ToUpper()} | {BuildThreatBand(SelectedTrack.ThreatLevel)}"
            : "NO TARGET SELECTED";
        SelectedTrackEnvelopeText = BuildEnvelopeSummary(SelectedTrack);
    }

    private void RefreshCommandStates()
    {
        DesignateSelectedCommand.NotifyCanExecuteChanged();
        FireSingleCommand.NotifyCanExecuteChanged();
        FireSalvoCommand.NotifyCanExecuteChanged();
    }

    private void RefreshUnreadCounts()
    {
        CommandUnreadCount = Sim.Comms.UnreadCount(RadioChannel.CommandNet);
        BatteryUnreadCount = Sim.Comms.UnreadCount(RadioChannel.BatteryNet);
        IntelUnreadCount = Sim.Comms.UnreadCount(RadioChannel.IntelNet);
        OnPropertyChanged(nameof(CommandChannelLabel));
        OnPropertyChanged(nameof(BatteryChannelLabel));
        OnPropertyChanged(nameof(IntelChannelLabel));
    }

    private void RefreshAiBindings()
    {
        OnPropertyChanged(nameof(AiStatusText));
        OnPropertyChanged(nameof(AiModelText));
        OnPropertyChanged(nameof(AiEndpointText));
    }

    private void RefreshDerivedBindings()
    {
        OnPropertyChanged(nameof(ThreatSummaryText));
        RefreshAiBindings();
    }

    private void ResetUiForScenarioLoad()
    {
        CommandNetMessages.Clear();
        BatteryNetMessages.Clear();
        IntelNetMessages.Clear();
        AllMessagesRecent.Clear();
        ThreatBoardTracks.Clear();
        Sim.Comms.MarkAllRead(RadioChannel.CommandNet);
        Sim.Comms.MarkAllRead(RadioChannel.BatteryNet);
        Sim.Comms.MarkAllRead(RadioChannel.IntelNet);
        SelectedTrackId = null;
        SelectedTrack = null;
        RefreshSelectedTrackReadout();
        RefreshUnreadCounts();
    }

    private static string BuildTrackBraa(TrackFile track) =>
        $"BRAA {track.BearingDeg:000}/{track.RangeNm:0.0} ANGELS {track.AltitudeFt / 1000:0.0} {track.AspectString.ToUpper()}";

    private static string BuildThreatBand(double threatLevel) => threatLevel switch
    {
        >= 0.7 => "HIGH THREAT",
        >= 0.4 => "MEDIUM THREAT",
        _ => "LOW THREAT"
    };

    private static string BuildEnvelopeSummary(TrackFile? track)
    {
        if (track == null)
            return "ENGAGEMENT WINDOW: STANDBY";

        return track.RangeNm switch
        {
            > 18 => "ENGAGEMENT WINDOW: OUTSIDE MAX RANGE",
            < 2.5 => "ENGAGEMENT WINDOW: BELOW MIN RANGE",
            _ => "ENGAGEMENT WINDOW: VALID"
        };
    }

    private static string BuildWeatherSummary(SimulationSnapshot snapshot) =>
        $"WX {snapshot.Weather.Description.ToUpper()} | VIS {snapshot.Weather.VisibilityNm:0}NM | CEIL {snapshot.Weather.CloudCeilingFt:0}FT";

    private static void DispatchToUI(Action action)
    {
        if (Application.Current?.Dispatcher.CheckAccess() == true)
            action();
        else
            Application.Current?.Dispatcher.BeginInvoke(action);
    }

    private static ScenarioDefinition CreateTutorialScenario() => new()
    {
        Name = "Tutorial — Single Bogey",
        Description = "A single enemy aircraft is approaching. Track, designate, and engage.",
        DurationMinutes = 10,
        PlayerBattery = new PlayerBatteryConfig
        {
            Callsign = "ALPHA", Type = "SA-11 BUK",
            Launchers = 4, ReserveMissiles = 12, RadarRangeNm = 80,
            EngagementRangeNm = 18, MissileType = "9M38", MissilePk = 0.70,
            InitialROE = "weapons_tight", InitialAlert = "yellow"
        },
        Command = new CommandConfig
        {
            Callsign = "ECHO", InitialROE = "weapons_tight", InitialAlert = "yellow",
            OpeningMessage = "ALPHA, ECHO. Single BOGEY detected bearing 045, range 120. " +
                             "Track and classify. WEAPONS TIGHT — do not engage until cleared. Acknowledge."
        },
        EnemyForces = new EnemyForcesConfig
        {
            Objective = "Strike friendly airfield",
            Waves = new List<WaveConfig>
            {
                new WaveConfig
                {
                    Trigger = "time", TimeMinutes = 0.5,
                    Aircraft = new List<AircraftSpawnConfig>
                    {
                        new AircraftSpawnConfig
                        {
                            Type = "SU-24", Count = 1,
                            SpawnBearingDeg = 45, SpawnRangeNm = 120,
                            SpawnAltitudeFt = 18000, SpawnHeadingDeg = 225,
                            SpawnSpeedKts = 450, InitialBehavior = "ingress_attack",
                            Aggressiveness = 0.5
                        }
                    }
                }
            }
        },
        VictoryConditions = new VictoryConditionsConfig
        {
            Win = "Destroy the enemy aircraft",
            Lose = "Enemy aircraft reaches target",
            MaxEnemyBreakthroughs = 0
        }
    };

    private static ScenarioDefinition CreateOperationDeadskyScenario() => new()
    {
        Name = "Operation DEADSKY",
        Description = "Layered hostile strike with escorts, decoys, and a late jammer push.",
        DurationMinutes = 24,
        PlayerBattery = new PlayerBatteryConfig
        {
            Callsign = "ALPHA", Type = "SA-11 BUK",
            Launchers = 4, ReserveMissiles = 16, RadarRangeNm = 120,
            EngagementRangeNm = 20, MissileType = "9M38", MissilePk = 0.68,
            InitialROE = "weapons_tight", InitialAlert = "yellow"
        },
        Command = new CommandConfig
        {
            Callsign = "ECHO", InitialROE = "weapons_tight", InitialAlert = "yellow",
            OpeningMessage = "ALPHA, ECHO. EXPECT MULTI-AXIS STRIKE PACKAGE. INITIAL ROE WEAPONS TIGHT. HOLD FIRE UNTIL DECLARE OR HOSTILE ACT CONFIRMED."
        },
        EnemyForces = new EnemyForcesConfig
        {
            Objective = "Penetrate sector air defenses and strike depot complex",
            Waves = new List<WaveConfig>
            {
                new WaveConfig
                {
                    Trigger = "time", TimeMinutes = 0.4,
                    Aircraft = new List<AircraftSpawnConfig>
                    {
                        new AircraftSpawnConfig
                        {
                            Type = "MiG-29", Count = 2,
                            SpawnBearingDeg = 30, SpawnRangeNm = 125,
                            SpawnAltitudeFt = 22000, SpawnHeadingDeg = 215,
                            SpawnSpeedKts = 480, InitialBehavior = "ingress_attack",
                            Aggressiveness = 0.65
                        }
                    }
                },
                new WaveConfig
                {
                    Trigger = "time", TimeMinutes = 2.1,
                    Aircraft = new List<AircraftSpawnConfig>
                    {
                        new AircraftSpawnConfig
                        {
                            Type = "MQ-9", Count = 2,
                            SpawnBearingDeg = 75, SpawnRangeNm = 118,
                            SpawnAltitudeFt = 14000, SpawnHeadingDeg = 240,
                            SpawnSpeedKts = 260, InitialBehavior = "feint",
                            Aggressiveness = 0.35
                        },
                        new AircraftSpawnConfig
                        {
                            Type = "SU-24", Count = 2,
                            SpawnBearingDeg = 58, SpawnRangeNm = 122,
                            SpawnAltitudeFt = 19000, SpawnHeadingDeg = 235,
                            SpawnSpeedKts = 440, InitialBehavior = "ingress_attack",
                            Aggressiveness = 0.72
                        }
                    }
                },
                new WaveConfig
                {
                    Trigger = "time", TimeMinutes = 5.8,
                    Aircraft = new List<AircraftSpawnConfig>
                    {
                        new AircraftSpawnConfig
                        {
                            Type = "SU-24", Count = 1,
                            SpawnBearingDeg = 110, SpawnRangeNm = 135,
                            SpawnAltitudeFt = 9000, SpawnHeadingDeg = 250,
                            SpawnSpeedKts = 430, InitialBehavior = "ecm_standoff",
                            Aggressiveness = 0.55
                        },
                        new AircraftSpawnConfig
                        {
                            Type = "MiG-29", Count = 2,
                            SpawnBearingDeg = 102, SpawnRangeNm = 128,
                            SpawnAltitudeFt = 17000, SpawnHeadingDeg = 248,
                            SpawnSpeedKts = 500, InitialBehavior = "terrain_following",
                            Aggressiveness = 0.76
                        }
                    }
                }
            }
        },
        VictoryConditions = new VictoryConditionsConfig
        {
            Win = "Break up the raid and keep the depot intact.",
            Lose = "Strike package breaks through to the depot complex.",
            MaxEnemyBreakthroughs = 1
        }
    };
}

// ── Child ViewModels ───────────────────────────────────────────────────────

public partial class LauncherViewModel : ObservableObject
{
    public string LauncherId { get; init; } = "";
    [ObservableProperty] private string _stateText = "READY";
    [ObservableProperty] private double _reloadProgress;
    [ObservableProperty] private string _missileType = "9M38";
    [ObservableProperty] private bool _isReady = true;

    public void Update(Launcher launcher)
    {
        StateText = launcher.State.ToString().ToUpper();
        ReloadProgress = launcher.ReloadProgress;
        MissileType = launcher.MissileType;
        IsReady = launcher.State == LauncherState.Ready;
    }
}

public partial class TrackRowViewModel : ObservableObject
{
    public string TrackId { get; private set; } = "";
    [ObservableProperty] private string _designation = "";
    [ObservableProperty] private string _classification = "";
    [ObservableProperty] private string _bearing = "";
    [ObservableProperty] private string _rangeText = "";
    [ObservableProperty] private string _altitudeText = "";
    [ObservableProperty] private string _speedText = "";
    [ObservableProperty] private string _threatText = "";
    [ObservableProperty] private string _rowColor = "#00DC00";
    [ObservableProperty] private Brush _rowBrush = Brushes.LimeGreen;
    [ObservableProperty] private string _aspect = "";

    public TrackRowViewModel(TrackFile track) { Update(track); }

    public void Update(TrackFile track)
    {
        TrackId = track.TrackId;
        Designation = track.TrackDesignation;
        Classification = track.Classification.ToString().ToUpper();
        Bearing = $"{track.BearingDeg:F0}°";
        RangeText = $"{track.RangeNm:F1}";
        AltitudeText = $"FL{track.AltitudeFt / 100:F0}";
        SpeedText = $"{track.SpeedKts:F0}";
        ThreatText = track.ThreatLevel > 0.7 ? "HIGH" : track.ThreatLevel > 0.4 ? "MED" : "LOW";
        Aspect = track.AspectString;
        RowColor = track.Classification switch
        {
            TrackClassification.Hostile or TrackClassification.AssumedHostile => "#FF5050",
            TrackClassification.Friendly => "#3296FF",
            _ => "#FFFF50"
        };
        RowBrush = (Brush)new BrushConverter().ConvertFromString(RowColor)!;
    }
}

public partial class SoldierViewModel : ObservableObject
{
    public string SoldierId { get; private set; }
    private readonly Soldier _soldier;

    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _roleText = "";
    [ObservableProperty] private double _morale;
    [ObservableProperty] private double _fear;
    [ObservableProperty] private double _proficiency;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _moraleColor = "#00DC00";

    public SoldierViewModel(Soldier soldier)
    {
        _soldier = soldier;
        SoldierId = soldier.Id;
        Refresh();
    }

    public void Refresh()
    {
        DisplayName = _soldier.FullName;
        RoleText = _soldier.Role.ToString();
        Morale = _soldier.Morale;
        Fear = _soldier.Fear;
        Proficiency = _soldier.Proficiency;
        Status = _soldier.Health.ToString();
        MoraleColor = _soldier.Morale > 0.7 ? "#00DC00" :
                      _soldier.Morale > 0.4 ? "#FFC800" : "#FF3232";
    }
}
