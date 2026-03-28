using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DEADSKY.AI.Scenario;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DEADSKY.AI.Agents;
using DEADSKY.AI.Client;
using DEADSKY.AI.Tools;
using DEADSKY.Audio;
using DEADSKY.Core.Campaign;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Economy;
using DEADSKY.Core.Entities;
using DEADSKY.Core.EnemyAI;
using DEADSKY.Core.Events;
using DEADSKY.Core.Logging;
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
public partial class MainViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private static readonly HashSet<string> VisibleLogCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "BRIEF",
        "MISSION",
        "WAVE",
        "CONTACT",
        "ENGAGEMENT",
        "LAUNCH",
        "ALERT",
        "ROE",
        "DAMAGE",
        "SUPPORT",
        "SECTOR",
        "REWARD",
        "REQ",
        "REQ-DENIED",
        "LOGISTICS",
        "SAVE"
    };

    // ── Core systems ──────────────────────────────────────────────────
    public SimulationEngine Sim { get; } = new();
    public AudioEngine Audio { get; } = new();
    public ScenarioManager Scenario { get; }
    public FriendlySupportDirector FriendlySupport { get; }
    public CrewRoster Crew { get; } = CrewRoster.CreateDefaultCrew();
    public BudgetSystem Budget { get; } = new();
    public PlayerProfile Profile { get; } = new();
    public EventEngine Events { get; }
    public AgentOrchestrator? AI { get; private set; }
    private readonly AIModelClient _aiClient = new();
    private GroupTacticManager _tactics = null!;
    private readonly List<SectorCampaignState> _sectorStates = new();
    private readonly object _uiTickSync = new();
    private SimulationSnapshot? _pendingUiSnapshot;
    private bool _uiTickUpdateQueued;
    private double _lastCrewRefreshGameTimeSec = double.NegativeInfinity;
    private double _lastSupportDisplayRefreshGameTimeSec = double.NegativeInfinity;
    private const double CrewRefreshIntervalSec = 0.5;
    private const double SupportDisplayRefreshIntervalSec = 0.5;

    // Event handler references for cleanup
    private Action<SimulationTickEvent>? _onSimTickHandler;
    private Action<NewContactEvent>? _onNewContactHandler;
    private Action<EngagementResultEvent>? _onEngagementResultHandler;
    private Action<MissileLaunchedEvent>? _onMissileLaunchedHandler;
    private Action<AlertLevelChangedEvent>? _onAlertChangedHandler;
    private Action<ROEChangedEvent>? _onROEChangedHandler;
    private Action<MissionCompletedEvent>? _onMissionCompletedHandler;
    private Action<BatteryDamagedEvent>? _onBatteryDamagedHandler;
    private Action<RadioMessage>? _onMessageReceivedHandler;
    private Action<Soldier, string>? _onCrewEventHandler;
    private Action<GameEvent>? _onGameEventFiredHandler;
    private Action<string>? _onWaveSpawnedHandler;
    private Action<ScenarioManager.MissionOutcome, string>? _onMissionCompleteHandler;

    // ── Observable state ──────────────────────────────────────────────
    [ObservableProperty] private SimulationSnapshot? _currentSnapshot;
    [ObservableProperty] private string _gameTime = "00:00:00 ZULU";
    [ObservableProperty] private string _alertLevelText = "YELLOW";
    [ObservableProperty] private string _roeText = "WEAPONS TIGHT";
    [ObservableProperty] private string _missionName = "AWAITING BRIEFING";
    [ObservableProperty] private bool _simulationRunning;
    [ObservableProperty] private MissionLifecycleState _missionLifecycle = MissionLifecycleState.Briefing;
    [ObservableProperty] private string? _selectedTrackId;
    [ObservableProperty] private TrackFile? _selectedTrack;
    [ObservableProperty] private string _statusMessage = "SYSTEM STANDBY";
    [ObservableProperty] private bool _aiAvailable;
    [ObservableProperty] private RadioChannel _activeChannel = RadioChannel.CommandNet;
    [ObservableProperty] private string _inputMessage = "";
    [ObservableProperty] private string _selectedTrackBraa = "BRAA ---";
    [ObservableProperty] private string _selectedTrackThreatText = "NO TARGET SELECTED";
    [ObservableProperty] private string _selectedTrackEnvelopeText = "ENGAGEMENT WINDOW: STANDBY";
    [ObservableProperty] private string _selectedTrackIdentityText = "IDENTITY: NONE";
    [ObservableProperty] private string _selectedTrackPackageText = "PACKAGE: NONE";
    [ObservableProperty] private string _selectedTrackRoleText = "ROLE: STANDBY";
    [ObservableProperty] private string _selectedTrackObjectiveText = "OBJECTIVE: STANDBY";
    [ObservableProperty] private string _selectedTrackDoctrineText = "DOCTRINE: NO TRACK SELECTED.";
    [ObservableProperty] private string _selectedTrackStatusText = "STATUS: STANDBY";
    [ObservableProperty] private string _selectedTrackMissionText = "MISSION: ---";
    [ObservableProperty] private string _selectedTrackControlText = "CONTROL: ---";
    [ObservableProperty] private string _selectedTrackKinematicsText = "KINEMATICS: ---";
    [ObservableProperty] private string _selectedTrackTimeToThreatText = "TIME TO THREAT: ---";
    [ObservableProperty] private string _radarCursorReadout = "CURSOR BRAA: ---";
    [ObservableProperty] private string _weatherSummary = "WX CLR | VIS 148km | CEIL 7620m";
    [ObservableProperty] private string _missionPhaseText = "MISSION PHASE: STANDBY";
    [ObservableProperty] private string _objectiveStatusText = "OBJECTIVE: AWAITING BRIEFING";
    [ObservableProperty] private string _recommendationText = "RECOMMENDATION: MAINTAIN SEARCH PATTERN";
    [ObservableProperty] private string _airPictureText = "PICTURE: AIRSPACE CLEAN";
    [ObservableProperty] private string _incomingThreatText = "INCOMING: NONE";
    [ObservableProperty] private string _objectiveBoardText = "OBJECTIVES: STANDBY";
    [ObservableProperty] private string _packageSummaryText = "PACKAGES: STANDBY";
    [ObservableProperty] private string _sectorEventText = "SECTOR EVENT: STANDBY";
    [ObservableProperty] private string _sectorLoreText = "SECTOR LORE: STANDBY";
    [ObservableProperty] private string _sectorIntegrityText = "SECTOR STATUS: NO OBJECTIVE STATE AVAILABLE.";
    [ObservableProperty] private string _afterActionSummaryText = "AFTER ACTION: NO RECORDED SORTIE.";
    [ObservableProperty] private string _theaterSupportText = "THEATER SUPPORT: FULL STOCKS AND NETWORK COVERAGE AVAILABLE.";
    [ObservableProperty] private int _commandUnreadCount;
    [ObservableProperty] private int _batteryUnreadCount;
    [ObservableProperty] private int _intelUnreadCount;
    [ObservableProperty] private bool _isCommandMenuOpen;
    [ObservableProperty] private bool _isRealisticModeActive;
    [ObservableProperty] private string _realisticModeSummary = "DYNAMIC SHIFT: ready to generate a fresh live scenario.";

    // Battery state
    [ObservableProperty] private int _readyLaunchers = 4;
    [ObservableProperty] private int _reserveMissiles = 12;
    [ObservableProperty] private int _confirmedKills;
    [ObservableProperty] private int _trackCount;
    [ObservableProperty] private int _hostileCount;
    [ObservableProperty] private string _batteryROE = "TIGHT";
    [ObservableProperty] private string _radarModeText = "SEARCH";
    [ObservableProperty] private string _radarPowerText = "POWER ON";
    [ObservableProperty] private string _radarEmissionText = "RADAR ACTIVE";
    [ObservableProperty] private string _radarCommsText = "COMMS GREEN";
    [ObservableProperty] private string _radarRangeStatusText = "RANGE 148km";
    [ObservableProperty] private string _radarSweepText = "SWEEP 000";
    [ObservableProperty] private string _trackHoldStatusText = "TRACK HOLD 0";
    [ObservableProperty] private bool _autoFocusSelectedTrack = true;
    [ObservableProperty] private bool _showRadarAltitudeLabels = true;
    [ObservableProperty] private bool _showRadarTrackTrails = true;
    [ObservableProperty] private bool _showTacticalMapLabels = true;

    // ── Collapsible section states ─────────────────────────────────────
    [ObservableProperty] private bool _isScopeControlExpanded = true;
    [ObservableProperty] private bool _isFireControlExpanded = true;
    [ObservableProperty] private bool _isWeaponsLoadoutExpanded = false;
    [ObservableProperty] private bool _isEngagementControlExpanded = true;
    [ObservableProperty] private bool _isMissionBoardExpanded = true;

    // Track previous values for auto-expand detection
    private int _previousReadyLaunchers = 4;
    private BatteryAlertLevel _previousAlertLevel = BatteryAlertLevel.Yellow;

    // Alert level color (bound to header)
    [ObservableProperty] private string _alertColor = "#FFC800";
    [ObservableProperty] private string _aiStatusColor = "#FFC800";

    // ── Message collections ────────────────────────────────────────────
    public ObservableCollection<RadioMessage> CommandNetMessages { get; } = new();
    public ObservableCollection<RadioMessage> BatteryNetMessages { get; } = new();
    public ObservableCollection<RadioMessage> IntelNetMessages { get; } = new();
    public ObservableCollection<RadioMessage> AllMessagesRecent { get; } = new();
    public ObservableCollection<OpsFeedItem> OpsFeed { get; } = new();

    // ── Launcher states ────────────────────────────────────────────────
    public ObservableCollection<LauncherViewModel> LauncherStates { get; } = new();

    // ── Threat board ───────────────────────────────────────────────────
    public ObservableCollection<TrackRowViewModel> ThreatBoardTracks { get; } = new();

    // ── Crew display ──────────────────────────────────────────────────
    public ObservableCollection<SoldierViewModel> CrewDisplay { get; } = new();
    public string CrewConditionSummaryText => Crew.BuildConditionSummary();
    public string CampaignTheaterText => $"THEATER RECORD: {CurrentSectorState?.TheaterName?.ToUpperInvariant() ?? "NO ACTIVE THEATER"}";
    public string CommandChannelLabel => CommandUnreadCount > 0 ? $"CMD {CommandUnreadCount}" : "CMD";
    public string BatteryChannelLabel => BatteryUnreadCount > 0 ? $"BAT {BatteryUnreadCount}" : "BAT";
    public string IntelChannelLabel => IntelUnreadCount > 0 ? $"INT {IntelUnreadCount}" : "INT";
    public string ThreatSummaryText => HostileCount == 0
        ? "AIR PICTURE CLEAN"
        : $"{HostileCount} HOSTILE / {TrackCount} TRACKS";
    public string MenuButtonText => IsCommandMenuOpen ? "CLOSE MENU" : "OPS MENU";
    public string SelectedTrackPanelTitle => SelectedTrackId == null ? "TRACK CONTROL" : $"TRACK {SelectedTrackId}";
    public string EngagementActionHintText => AssessFireControlState(Sim.LatestSnapshot.Battery, SelectedTrack).HintText;
    public string TrackHoldButtonText => SelectedTrack?.IsTrackHeld == true || SelectedTrack?.IsDesignated == true
        ? "TRACK HELD"
        : "HOLD TRACK";
    public string MissionPrimaryActionText => MissionLifecycle switch
    {
        MissionLifecycleState.Briefing => "ENTER STATION",
        MissionLifecycleState.Standby => "START OPERATION",
        MissionLifecycleState.Active => "STATION LIVE",
        MissionLifecycleState.Paused => "RESUME OPERATION",
        MissionLifecycleState.Debrief => "RESET SCENARIO",
        _ => "ENTER STATION"
    };
    public string MissionPauseHintText => MissionLifecycle switch
    {
        MissionLifecycleState.Active => "Pause the live station here if you need to review the picture or reload the scenario.",
        MissionLifecycleState.Paused => "The station is paused. Resume here, or press ESC to return to the operator view.",
        MissionLifecycleState.Debrief => "Mission complete. Reload the tutorial, restart the operation, or spin up a fresh dynamic shift.",
        _ => "Mission flow stays here. ESC toggles this menu at any time."
    };
    public string OperationModeText => IsRealisticModeActive ? "LIVE SHIFT" : "SCRIPTED OP";
    public string RealisticModeStatusText => RealisticModeSummary;
    public ScenarioDefinition? CurrentScenarioDefinition => Scenario.CurrentScenario;

    // ── Constructor ───────────────────────────────────────────────────

    public MainViewModel()
    {
        Sim.Crew = Crew;
        FriendlySupport = new FriendlySupportDirector(Sim.Comms, Sim.Entities, Sim.Radar);
        Scenario = new ScenarioManager(Sim, _tactics);
        Events = new EventEngine(Sim, Crew);
        _tactics = new GroupTacticManager(Sim.Entities);

        InitializeSystems();
        InitializeRequisitionTerminal();
        LoadCampaignState();
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
        // Store handler references for proper cleanup in Dispose()
        _onSimTickHandler = OnSimTick;
        _onNewContactHandler = OnNewContact;
        _onEngagementResultHandler = OnEngagementResult;
        _onMissileLaunchedHandler = OnMissileLaunched;
        _onAlertChangedHandler = OnAlertChanged;
        _onROEChangedHandler = OnROEChanged;
        _onMissionCompletedHandler = OnMissionCompleted;
        _onBatteryDamagedHandler = OnBatteryDamaged;

        Sim.Events.Subscribe(_onSimTickHandler);
        Sim.Events.Subscribe(_onNewContactHandler);
        Sim.Events.Subscribe(_onEngagementResultHandler);
        Sim.Events.Subscribe(_onMissileLaunchedHandler);
        Sim.Events.Subscribe(_onAlertChangedHandler);
        Sim.Events.Subscribe(_onROEChangedHandler);
        Sim.Events.Subscribe(_onMissionCompletedHandler);
        Sim.Events.Subscribe(_onBatteryDamagedHandler);

        _onMessageReceivedHandler = OnMessageReceived;
        Sim.Comms.MessageReceived += _onMessageReceivedHandler;

        _onCrewEventHandler = OnCrewEvent;
        Crew.CrewEventOccurred += _onCrewEventHandler;
        
        _onGameEventFiredHandler = OnGameEventFired;
        Events.EventFired += _onGameEventFiredHandler;

        _onWaveSpawnedHandler = wave =>
        {
            DispatchToUI(() => SetStatus($"NEW THREAT: {wave}"));
            LogOps("WAVE", wave);
            Sim.Comms.Queue(CommManager.CreateInterceptMessage($"RAVEN ACTUAL coordinating {wave}."));
        };
        Scenario.OnWaveSpawned += _onWaveSpawnedHandler;
        
        _onMissionCompleteHandler = (outcome, reason) =>
            DispatchToUI(() => OnMissionEnd(outcome, reason));
        Scenario.OnMissionComplete += _onMissionCompleteHandler;
    }

    private async void InitAI()
    {
        var toolRegistry = new ToolRegistry(Sim, _tactics, FriendlySupport, () => Scenario.CurrentScenario);
        var commanderProfile = EnemyCommanderProfile.ForChapter(1);

        AI = new AgentOrchestrator(_aiClient, toolRegistry, commanderProfile, _tactics, FriendlySupport, () => Scenario.CurrentScenario);

        bool available = await AI.CheckAvailabilityAsync();
        AiAvailable = available;
        AiStatusColor = available ? "#00DC00" : "#FFC800";
        RefreshCommandStates();
        RefreshDerivedBindings();
        if (!available)
            SetStatus("LM STUDIO MODEL NOT READY");

        // Hook AI into sim tick
        Sim.OnTickForAI = snapshot => AI.Tick(0.1, snapshot);

        // Subscribe to new contacts for intel agent
        // IMPORTANT: Fire-and-forget pattern used here to prevent blocking the simulation thread.
        // The EventBus invokes handlers synchronously on the simulation thread, so we must NOT await.
        // AI.HandleNewContactAsync() already has internal busy checks to prevent concurrent execution.
        Sim.Events.Subscribe<NewContactEvent>(evt =>
        {
            if (AI?.AIAvailable == true)
            {
                // Fire-and-forget: initiate async AI processing without blocking
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await AI.HandleNewContactAsync(evt.TrackId, Sim.LatestSnapshot);
                    }
                    catch (Exception ex)
                    {
                        // Use Console.Error instead of Debug.WriteLine for Release builds
                        Console.Error.WriteLine($"AI HandleNewContactAsync error: {ex.Message}");
                        Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                });
            }
        });
    }

    private void PopulateCrewDisplay()
    {
        foreach (var s in Crew.Soldiers)
            CrewDisplay.Add(new SoldierViewModel(s));
    }

    private SectorCampaignState? CurrentSectorState => Scenario.CurrentScenario == null
        ? null
        : _sectorStates.FirstOrDefault(state =>
            state.TheaterName.Equals(Scenario.CurrentScenario.SectorMap.TheaterName, StringComparison.OrdinalIgnoreCase));

    private void RefreshCrewDisplay()
    {
        foreach (var vm in CrewDisplay)
            vm.Refresh();

        OnPropertyChanged(nameof(CrewConditionSummaryText));
    }

    private void EnsureSectorStateForCurrentScenario()
    {
        if (Scenario.CurrentScenario == null)
            return;

        var existing = CurrentSectorState;
        var ensured = SectorCampaignDirector.EnsureScenarioState(existing, Scenario.CurrentScenario);
        if (existing == null)
            _sectorStates.Add(ensured);

        RefreshSectorStateDisplay();
    }

    private void RefreshSectorStateDisplay()
    {
        SectorIntegrityText = SectorCampaignDirector.BuildIntegritySummary(CurrentSectorState);
        AfterActionSummaryText = CurrentSectorState?.LastAfterActionSummary ?? "AFTER ACTION: NO RECORDED SORTIE.";
        OnPropertyChanged(nameof(CampaignTheaterText));
    }

    private void ApplyTheaterSupportAdjustments()
    {
        if (Scenario.CurrentScenario == null)
        {
            TheaterSupportText = "THEATER SUPPORT: FULL STOCKS AND NETWORK COVERAGE AVAILABLE.";
            return;
        }

        var adjustment = TheaterSupportDirector.Apply(CurrentSectorState, Scenario.CurrentScenario, Sim);
        TheaterSupportText = adjustment.Summary;

        if (adjustment.MissileReservePenalty > 0 || adjustment.CommandNetStrained || adjustment.PkModifier < 0)
        {
            LogOps("SUPPORT", adjustment.Summary);
            Sim.Comms.Queue(CommManager.CreateAlliedHQMessage(adjustment.Summary, MessagePriority.Priority));
        }
    }

    // ── Simulation control commands ───────────────────────────────────

    [RelayCommand]
    public async Task RefreshAiStatus()
    {
        if (AI == null)
            return;

        AiAvailable = await AI.CheckAvailabilityAsync();
        AiStatusColor = AiAvailable ? "#00DC00" : "#FFC800";
        RefreshCommandStates();
        RefreshDerivedBindings();
        SetStatus(AiAvailable
            ? "LM STUDIO MODEL READY"
            : "LM STUDIO MODEL NOT READY");
    }

    [RelayCommand]
    public void ToggleCommandMenu()
    {
        IsCommandMenuOpen = !IsCommandMenuOpen;
        Audio.Play(SoundEvent.UiMenuOpen);
    }

    [RelayCommand]
    public void CloseCommandMenu()
    {
        IsCommandMenuOpen = false;
        Audio.Play(SoundEvent.UiMenuOpen);
    }

    public async Task LoadRealisticMode()
    {
        if (AI == null || !AiAvailable)
        {
            RealisticModeSummary = "DYNAMIC SHIFT: LM Studio model not ready. Load the model before generating a live operation.";
            SetStatus("LM STUDIO MODEL REQUIRED");
            RefreshDerivedBindings();
            return;
        }

        try
        {
            SetStatus("GENERATING DYNAMIC SHIFT...");
            RealisticModeSummary = "DYNAMIC SHIFT: building a fresh theater contract and materializing a live operation.";
            RefreshDerivedBindings();

            var generator = new RealisticScenarioGenerator(_aiClient);
            string theaterContext = BuildRealisticModeContext();
            var result = await generator.GenerateAsync(theaterContext, allowFallback: false);

            if (result == null || result.Scenario == null)
            {
                string reason = result?.FailureReason ?? "SCENARIO OUTPUT INVALID OR UNAVAILABLE.";
                string detail = result?.ValidationErrors.Count > 0
                    ? $" {string.Join(" | ", result.ValidationErrors)}"
                    : string.Empty;
                RealisticModeSummary = $"DYNAMIC SHIFT: generation failed. {reason}.{detail}";
                SetStatus("DYNAMIC SHIFT FAILED");
                RefreshDerivedBindings();
                return;
            }

            LoadScenarioDefinition(result.Scenario, isRealisticMode: true);
            string warningTag = result.ValidationErrors.Count > 0
                ? $" FIXUPS: {string.Join(" | ", result.ValidationErrors)}"
                : string.Empty;
            RealisticModeSummary = $"DYNAMIC SHIFT: {result.Validation.Summary}. {result.Summary}{warningTag}";
            SetStatus($"DYNAMIC SHIFT LOADED: {result.Scenario.Name}");
            RefreshDerivedBindings();
        }
        catch (Exception ex)
        {
            RealisticModeSummary = $"DYNAMIC SHIFT: generation error. {ex.Message}";
            SetStatus("DYNAMIC SHIFT ERROR");
            RefreshDerivedBindings();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartMission))]
    public void StartMission()
    {
        if (MissionLifecycle == MissionLifecycleState.Active)
            return;

        if (!IsAiReady())
        {
            SetStatus("LM STUDIO MODEL REQUIRED");
            IsCommandMenuOpen = true;
            return;
        }

        FlushPendingRadioTraffic(); // Deliver queued messages when mission starts
        Sim.Start();
        SetMissionLifecycle(MissionLifecycleState.Active);
        IsCommandMenuOpen = false;
        SetStatus("MISSION ACTIVE");
        Audio.Play(SoundEvent.SystemOnline);
        LogOps("MISSION", "Battery transitioned to active combat operations.");
    }

    private bool CanStartMission() =>
        CurrentSnapshot != null &&
        AiAvailable &&
        MissionLifecycle is MissionLifecycleState.Briefing or MissionLifecycleState.Standby or MissionLifecycleState.Paused;

    [RelayCommand(CanExecute = nameof(CanPauseMission))]
    public void PauseMission()
    {
        if (MissionLifecycle != MissionLifecycleState.Active)
            return;

        Sim.Pause();
        SetMissionLifecycle(MissionLifecycleState.Paused);
        IsCommandMenuOpen = true;
        SetStatus("MISSION PAUSED");
        LogOps("MISSION", "Battery paused for command review.");
    }

    private bool CanPauseMission() => MissionLifecycle == MissionLifecycleState.Active;

    [RelayCommand]
    public void LoadScenario(string scenarioPath)
    {
        try
        {
            var loader = new ScenarioLoader();
            var scenarioKey = scenarioPath?.Trim().ToLowerInvariant();
            ScenarioDefinition scenario = scenarioKey switch
            {
                null or "" => ResolveScenarioDefinition("tutorial"),
                "startup" => ResolveScenarioDefinition(ChooseStartupScenarioKey()),
                _ when ScriptedScenarioFactories.ContainsKey(scenarioKey!) => ResolveScenarioDefinition(scenarioKey),
                _ when File.Exists(scenarioPath) => loader.Load(scenarioPath),
                _ => ResolveScenarioDefinition("tutorial")
            };

            LoadScenarioDefinition(scenario, isRealisticMode: scenario.IsRealisticMode);
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
            RefreshFromSimulationSnapshot();
            SetStatus($"DESIGNATED: {SelectedTrackId}");
            Audio.Play(SoundEvent.LockOnWarning);
            RefreshDerivedBindings();
        }
    }

    private bool CanDesignate() => SelectedTrackId != null && SimulationRunning;

    [RelayCommand(CanExecute = nameof(CanHoldSelectedTrack))]
    public void ToggleTrackHoldSelected()
    {
        if (SelectedTrackId == null)
            return;

        bool ok = Sim.PlayerToggleTrackHold(SelectedTrackId);
        RefreshFromSimulationSnapshot();
        if (ok)
        {
            bool isHeld = SelectedTrack?.IsTrackHeld == true || SelectedTrack?.IsDesignated == true;
            SetStatus(isHeld ? $"TRACK HELD: {SelectedTrackId}" : $"TRACK MEMORY CLEARED: {SelectedTrackId}");
            Audio.Play(SoundEvent.UiButtonPress);
        }

        RefreshDerivedBindings();
    }

    private bool CanHoldSelectedTrack() => SelectedTrackId != null && SimulationRunning;

    [RelayCommand(CanExecute = nameof(CanReleaseSelectedTrack))]
    public void ReleaseSelectedTrack()
    {
        if (SelectedTrackId == null)
            return;

        bool ok = Sim.PlayerReleaseTrack(SelectedTrackId);
        RefreshFromSimulationSnapshot();
        if (ok)
        {
            SetStatus($"TRACK RELEASED: {SelectedTrackId}");
            Audio.Play(SoundEvent.UiButtonPress);
        }

        RefreshDerivedBindings();
    }

    private bool CanReleaseSelectedTrack() =>
        SelectedTrackId != null &&
        SimulationRunning &&
        SelectedTrack != null &&
        (SelectedTrack.IsTrackHeld || SelectedTrack.IsDesignated);

    [RelayCommand(CanExecute = nameof(CanFire))]
    public void FireSingle()
    {
        if (SelectedTrackId == null) return;
        bool fired = Sim.PlayerFire(SelectedTrackId);
        RefreshFromSimulationSnapshot();
        if (fired)
        {
            Audio.Play(SoundEvent.MissileLaunch);
            SetStatus($"MISSILE AWAY - {SelectedTrackId}");
            GameSessionLogger.Current?.OnPlayerAction("FIRE SINGLE", $"Track {SelectedTrackId} {SelectedTrack?.TrackDesignation} RNG {SelectedTrack?.RangeNm:0.0}nm");
        }
        else
        {
            SetStatus($"FIRE DENIED - {Sim.Weapons.LastError}");
            GameSessionLogger.Current?.OnPlayerAction("FIRE DENIED", Sim.Weapons.LastError);
        }

        RefreshDerivedBindings();
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
        RefreshFromSimulationSnapshot();
        if (fired > 0)
        {
            Audio.Play(SoundEvent.MissileLaunch);
            SetStatus($"SALVO x{fired} AWAY - {SelectedTrackId}");
            GameSessionLogger.Current?.OnPlayerAction($"FIRE SALVO x{fired}", $"Track {SelectedTrackId} {SelectedTrack?.TrackDesignation} RNG {SelectedTrack?.RangeNm:0.0}nm");
        }
        else if (!string.IsNullOrWhiteSpace(Sim.Weapons.LastError))
        {
            SetStatus($"FIRE DENIED - {Sim.Weapons.LastError}");
            GameSessionLogger.Current?.OnPlayerAction("SALVO DENIED", Sim.Weapons.LastError);
        }

        RefreshDerivedBindings();
    }

    // ── Radar commands ────────────────────────────────────────────────

    [RelayCommand]
    public void SetRadarSearch()
    {
        Sim.PlayerSetRadarMode(RadarMode.Search);
        RefreshFromSimulationSnapshot();
        SetStatus("RADAR MODE SEARCH");
        Audio.Play(SoundEvent.UiButtonPress);
        RefreshDerivedBindings();
    }

    [RelayCommand]
    public void SetRadarTWS()
    {
        Sim.PlayerSetRadarMode(RadarMode.TrackWhileScan);
        RefreshFromSimulationSnapshot();
        SetStatus("RADAR MODE TWS");
        Audio.Play(SoundEvent.UiButtonPress);
        RefreshDerivedBindings();
    }

    [RelayCommand]
    public void SetRadarSilent()
    {
        Sim.PlayerSetRadarMode(RadarMode.Silent);
        RefreshFromSimulationSnapshot();
        SetStatus("RADAR MODE SILENT");
        Audio.Play(SoundEvent.UiButtonPress);
        RefreshDerivedBindings();
    }

    [RelayCommand]
    public void SetRadarStandby()
    {
        Sim.PlayerSetRadarMode(RadarMode.Standby);
        RefreshFromSimulationSnapshot();
        SetStatus("RADAR MODE STANDBY");
        Audio.Play(SoundEvent.UiButtonPress);
        RefreshDerivedBindings();
    }

    [RelayCommand]
    public void SetRadarRange(double rangeNm)
    {
        double currentRange = Sim.LatestSnapshot.RadarRangeNm;
        double normalizedRange = SimulationEngine.NormalizeRadarRange(rangeNm);
        if (Math.Abs(normalizedRange - currentRange) < 0.1)
        {
            SetStatus($"RADAR RANGE {currentRange:F0} NM");
            RefreshDerivedBindings();
            return;
        }

        Sim.PlayerSetRadarRange(normalizedRange);
        RefreshFromSimulationSnapshot();
        SetStatus($"RADAR RANGE SET {Sim.LatestSnapshot.RadarRangeNm:F0} NM");
        Audio.Play(SoundEvent.UiButtonPress);
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

    [RelayCommand(CanExecute = nameof(CanSendQuickCommand))]
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

    private bool CanSendQuickCommand(string? commandKey) => commandKey switch
    {
        "ack" => true,
        "picture" => SimulationRunning,
        "status" => CurrentSnapshot != null,
        "declare" => SimulationRunning && SelectedTrack != null,
        "weapons_free" => SimulationRunning && HostileCount > 0,
        "intel" => HostileCount > 0,
        "awacs" => SimulationRunning,
        "cap" => SimulationRunning,
        "jam" => SimulationRunning,
        "battery" => SimulationRunning,
        _ => false
    };

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
        FocusSelectedTrackIfNeeded();
        RefreshSelectedTrackReadout();
        Audio.Play(SoundEvent.UiButtonPress);
        RefreshCommandStates();
    }

    public void CycleTrackSelection(int direction)
    {
        if (ThreatBoardTracks.Count == 0)
            return;

        int currentIndex = SelectedTrackId == null
            ? -1
            : ThreatBoardTracks.ToList().FindIndex(track => track.TrackId == SelectedTrackId);
        int nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + direction + ThreatBoardTracks.Count) % ThreatBoardTracks.Count;

        SelectTrack(ThreatBoardTracks[nextIndex].TrackId);
    }

    public void SelectRadarPoint(double bearingDeg, double rangeNm)
    {
        RadarCursorReadout = $"CURSOR BRAA {bearingDeg:000}/{rangeNm * 1.852:0.0}km";
        SetStatus($"CURSOR BRAA {bearingDeg:000}/{rangeNm * 1.852:0.0}km");
    }

    private void FocusSelectedTrackIfNeeded()
    {
        if (SelectedTrack == null || !AutoFocusSelectedTrack)
            return;

        double focusRange = ChooseTrackFocusRange(SelectedTrack.RangeNm);
        if (Math.Abs(Sim.LatestSnapshot.RadarRangeNm - focusRange) < 0.1)
            return;

        Sim.PlayerSetRadarRange(focusRange);
        RadarCursorReadout = $"TRACK FOCUS {SelectedTrack.BearingDeg:000}/{SelectedTrack.RangeNm * 1.852:0.0}km";
    }

    private static double ChooseTrackFocusRange(double trackRangeNm)
    {
        double desiredRange = trackRangeNm * 1.35;
        if (desiredRange <= 40)
            return 40;
        if (desiredRange <= 80)
            return 80;
        return 120;
    }

    // ── Simulation event handlers (called on sim thread) ─────────────

    private void OnSimTick(SimulationTickEvent evt)
    {
        var snapshot = Sim.LatestSnapshot;
        Scenario.Update(evt.GameTimeSec);
        FriendlySupport.Tick(evt.DeltaTime, evt.GameTimeSec, snapshot);
        FriendlySupport.UpdateOperationalContext(Scenario.CurrentScenario, snapshot, Sim.Weapons.Incidents);
        Events.Update(evt.DeltaTime);
        Crew.Update(evt.DeltaTime,
            underFire: snapshot.HostileTracks.Any(t => t.RangeNm < 25),
            recentKill: false, recentLoss: false);
        QueueLiveUiRefresh(snapshot);
    }

    private void QueueLiveUiRefresh(SimulationSnapshot snapshot)
    {
        lock (_uiTickSync)
        {
            _pendingUiSnapshot = snapshot;
            if (_uiTickUpdateQueued)
                return;

            _uiTickUpdateQueued = true;
        }

        DispatchToUI(ProcessQueuedLiveUiRefresh);
    }

    private void ProcessQueuedLiveUiRefresh()
    {
        SimulationSnapshot snapshot;
        lock (_uiTickSync)
        {
            snapshot = _pendingUiSnapshot ?? Sim.LatestSnapshot;
            _pendingUiSnapshot = null;
        }

        UpdateFromSnapshot(snapshot, isLiveTick: true);
        RefreshThreatWarnings(snapshot);

        if (snapshot.GameTimeSec - _lastCrewRefreshGameTimeSec >= CrewRefreshIntervalSec)
        {
            RefreshCrewDisplay();
            _lastCrewRefreshGameTimeSec = snapshot.GameTimeSec;
        }

        if (snapshot.GameTimeSec - _lastSupportDisplayRefreshGameTimeSec >= SupportDisplayRefreshIntervalSec)
        {
            RefreshSupportDisplay();
            _lastSupportDisplayRefreshGameTimeSec = snapshot.GameTimeSec;
        }

        RefreshSupportNetworkReports(snapshot);

        bool shouldQueueAgain;
        lock (_uiTickSync)
        {
            _uiTickUpdateQueued = false;
            shouldQueueAgain = _pendingUiSnapshot != null;
            if (shouldQueueAgain)
                _uiTickUpdateQueued = true;
        }

        if (shouldQueueAgain)
            DispatchToUI(ProcessQueuedLiveUiRefresh);
    }

    private void UpdateFromSnapshot(SimulationSnapshot snapshot, bool isLiveTick = false)
    {
        CurrentSnapshot = snapshot;
        GameTime = snapshot.GameTimeString;
        WeatherSummary = BuildWeatherSummary(snapshot);

        var battery = snapshot.Battery;
        if (battery != null)
        {
            // Check for launcher reload completion (auto-expand Weapons/Loadout section)
            int currentReadyLaunchers = battery.ReadyLaunchers;
            if (currentReadyLaunchers > _previousReadyLaunchers)
            {
                // A launcher just finished reloading
                IsWeaponsLoadoutExpanded = true;
            }
            _previousReadyLaunchers = currentReadyLaunchers;
            
            ReadyLaunchers = battery.ReadyLaunchers;
            ReserveMissiles = battery.ReserveMissiles;
            ConfirmedKills = battery.ConfirmedKills;
            
            // Check for alert level change to HIGH or CRITICAL (auto-expand Mission Board section)
            BatteryAlertLevel currentAlertLevel = battery.AlertLevel;
            if (currentAlertLevel != _previousAlertLevel)
            {
                // Auto-expand Mission Board when alert level changes to Orange (HIGH), Red (CRITICAL), or Black (CRITICAL)
                if (currentAlertLevel == BatteryAlertLevel.Orange || 
                    currentAlertLevel == BatteryAlertLevel.Red || 
                    currentAlertLevel == BatteryAlertLevel.Black)
                {
                    IsMissionBoardExpanded = true;
                }
                _previousAlertLevel = currentAlertLevel;
            }
            
            AlertLevelText = battery.AlertLevel.ToString().ToUpper();
            RoeText = battery.ROE.ToString().Replace("Weapons", "WEAPONS ").ToUpper();
            RadarModeText = battery.RadarMode.ToString().ToUpper();
            RadarPowerText = battery.PowerOnline ? "POWER ON" : "POWER OFF";
            RadarEmissionText = battery.RadarOnline
                ? $"RADAR {battery.RadarMode.ToString().ToUpper()}"
                : $"RADAR {battery.RadarMode.ToString().ToUpper()} / OFF AIR";
            RadarCommsText = battery.CommsOnline ? "COMMS GREEN" : "COMMS DEGRADED";
            RadarRangeStatusText = $"RANGE {snapshot.RadarRangeNm * 1.852:0}km";
            RadarSweepText = $"SWEEP {snapshot.RadarSweepAngle:000}";
            TrackHoldStatusText = $"TRACK HOLD {snapshot.AllTracks.Count(track => track.IsTrackHeld || track.IsDesignated)}";
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
        UpdateBattlePicture(snapshot);

        // Update threat board
        UpdateThreatBoard(snapshot);

        // Update selected track
        if (SelectedTrackId != null)
            SelectedTrack = snapshot.AllTracks.FirstOrDefault(t => t.TrackId == SelectedTrackId);

        RefreshFireControlFeedback(snapshot);
        RefreshWeaponState(snapshot);
        RefreshSelectedTrackReadout();
        RefreshAssessment(snapshot);
        RefreshCommandStates();
        if (isLiveTick)
            RefreshLiveBindings();
        else
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
        GameLogger.Info("UI-EVENT", $"OnNewContact received: {evt.TrackId} [{evt.Classification}] on thread {Environment.CurrentManagedThreadId}");
        
        DispatchToUI(() =>
        {
            GameLogger.Info("UI-EVENT", $"OnNewContact UI dispatch executing for {evt.TrackId}");
            try
            {
                Audio.Play(SoundEvent.NewContact);
                SetStatus($"NEW CONTACT: {evt.TrackId} [{evt.Classification}]");
                PushNotification("RADAR", "NEW CONTACT", $"{evt.TrackId} classified {evt.Classification}.", NotificationSeverity.Warning);
                LogOps("CONTACT", $"{evt.TrackId} classified {evt.Classification}.");
                
                // Auto-expand Engagement Control section when a new threat is detected
                IsEngagementControlExpanded = true;
                
                GameLogger.Info("UI-EVENT", $"OnNewContact completed successfully for {evt.TrackId}");
            }
            catch (Exception ex)
            {
                GameLogger.Error("UI-EVENT", $"OnNewContact failed for {evt.TrackId}", ex);
            }
        });
    }

    private void OnEngagementResult(EngagementResultEvent evt)
    {
        DispatchToUI(() =>
        {
            if (evt.WasKill)
            {
                Audio.Play(SoundEvent.MissileImpact);
                PushNotification("ENGAGE", "TARGET DESTROYED", $"{evt.TrackId} has been destroyed.", NotificationSeverity.Info);
                SetStatus($"SPLASH - {evt.TrackId} DESTROYED");
                Crew.ApplyMoraleBoost(0.05, "Kill confirmed");
                LogOps("ENGAGEMENT", $"{evt.TrackId} destroyed.");
            }
            else
            {
                Audio.Play(SoundEvent.MissileMiss);
                PushNotification("ENGAGE", "MISS", $"{evt.TrackId} survived the engagement.", NotificationSeverity.Warning);
                SetStatus($"MISS - {evt.TrackId}");
                LogOps("ENGAGEMENT", $"{evt.TrackId} survived missile engagement.");
            }
        });
    }

    private void OnMissileLaunched(MissileLaunchedEvent evt)
    {
        DispatchToUI(() =>
        {
            SetStatus($"MISSILE AWAY -> {evt.TargetTrackId} [{evt.LauncherId}]");
            PushNotification("LAUNCH", "MISSILE AWAY", $"{evt.LauncherId} engaged {evt.TargetTrackId}.", NotificationSeverity.Info);
            LogOps("LAUNCH", $"{evt.LauncherId} engaged {evt.TargetTrackId}.");
        });
    }

    private void OnAlertChanged(AlertLevelChangedEvent evt)
    {
        DispatchToUI(() =>
        {
            if (evt.NewLevel is "Red" or "Black")
                Audio.Play(SoundEvent.AlertKlaxon);
            SetStatus($"ALERT LEVEL: {evt.NewLevel.ToUpper()}");
            PushNotification("ALERT", $"ALERT {evt.NewLevel.ToUpper()}", "Battery alert posture changed.", evt.NewLevel is "Red" or "Black" ? NotificationSeverity.Critical : NotificationSeverity.Warning);
            LogOps("ALERT", $"Alert level changed to {evt.NewLevel.ToUpper()}.");
        });
    }

    private void OnROEChanged(ROEChangedEvent evt)
    {
        DispatchToUI(() =>
        {
            Audio.Play(SoundEvent.FlashMessageAlert);
            SetStatus($"ROE CHANGE: {evt.NewROE.Replace("Weapons", "WEAPONS ")}");
            PushNotification("ROE", "RULES UPDATED", $"{evt.NewROE.Replace("Weapons", "WEAPONS ")} authorized by {evt.AuthorizedBy}.", NotificationSeverity.Warning);
            LogOps("ROE", $"ROE updated to {evt.NewROE.Replace("Weapons", "WEAPONS ")} by {evt.AuthorizedBy}.");
            
            // Auto-expand Fire Control section when ROE changes to WEAPONS TIGHT or WEAPONS FREE
            if (evt.NewROE.Contains("WeaponsTight", StringComparison.OrdinalIgnoreCase) ||
                evt.NewROE.Contains("WeaponsFree", StringComparison.OrdinalIgnoreCase))
            {
                IsFireControlExpanded = true;
            }
        });
    }

    private void OnMissionCompleted(MissionCompletedEvent evt)
    {
        DispatchToUI(() =>
        {
            string outcome = evt.Victory ? "VICTORY" : "DEFEAT";
            SetStatus($"MISSION {outcome}: {evt.Reason}");
            Sim.Pause();
            SetMissionLifecycle(MissionLifecycleState.Debrief);
            IsCommandMenuOpen = true;
            PushNotification("MISSION", outcome, evt.Reason, evt.Victory ? NotificationSeverity.Info : NotificationSeverity.Critical, durationSeconds: 12);
            LogOps("MISSION", $"{outcome}: {evt.Reason}");
        });
    }

    private void OnBatteryDamaged(BatteryDamagedEvent evt)
    {
        DispatchToUI(() =>
        {
            Audio.Play(SoundEvent.AlertKlaxon);
            SetStatus($"BATTERY HIT: {evt.ComponentDamaged}");
            Crew.ApplyFearEvent(0.3, "battery hit");
            PushNotification("DAMAGE", "BATTERY HIT", evt.ComponentDamaged, NotificationSeverity.Critical, durationSeconds: 10);
            LogOps("DAMAGE", $"Battery component hit: {evt.ComponentDamaged}.");
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
                RadioChannel.AirDefenseNet => AirDefenseNetMessages,
                RadioChannel.IntelNet => IntelNetMessages,
                RadioChannel.Guard => GuardMessages,
                RadioChannel.OpenFreq => OpenFrequencyMessages,
                _ => null
            };
            target?.Add(msg);
            msg.IsRead = CanReadActiveRadioChannel && msg.Channel == ActiveChannel;
            AllMessagesRecent.Add(msg);
            if (AllMessagesRecent.Count > 100)
                AllMessagesRecent.RemoveAt(0);
            if (msg.RequiresAttention)
            {
                PriorityOverlayMessages.Insert(0, msg);
                while (PriorityOverlayMessages.Count > 8)
                    PriorityOverlayMessages.RemoveAt(PriorityOverlayMessages.Count - 1);
                PushRadioNotification(msg);
            }
            RefreshUnreadCounts();
            RefreshReplyStates();

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
            OnPropertyChanged(nameof(CrewConditionSummaryText));
        });
    }

    private void OnGameEventFired(GameEvent evt)
    {
        DispatchToUI(() =>
        {
            if (evt.Category is GameEventCategory.RadarMalfunction)
            {
                Audio.Play(SoundEvent.SystemOffline);
                PushNotification("SYSTEM", "RADAR DEGRADED", evt.Description, NotificationSeverity.Critical, durationSeconds: 10);
            }
        });
    }

    private void OnMissionEnd(ScenarioManager.MissionOutcome outcome, string reason)
    {
        string outcomeStr = outcome == ScenarioManager.MissionOutcome.Victory ? "VICTORY" :
                            outcome == ScenarioManager.MissionOutcome.PartialVictory ? "PARTIAL VICTORY" : "DEFEAT";
        SetStatus($"{outcomeStr}: {reason}");
        SetMissionLifecycle(MissionLifecycleState.Debrief);
        IsCommandMenuOpen = true;

        // Award budget
        var battery = Sim.Entities.GetPlayerBattery();
        if (battery != null && Scenario.CurrentScenario != null)
        {
            var sectorResolution = SectorCampaignDirector.ApplyMissionOutcome(
                CurrentSectorState,
                Scenario.CurrentScenario,
                Sim.LatestSnapshot,
                outcome,
                Scenario.EnemyBreakthroughs);
            if (CurrentSectorState == null)
                _sectorStates.Add(sectorResolution.State);

            var reward = MissionRewardCalculator.Calculate(
                5000, battery.ConfirmedKills, battery.MissilesFired,
                Crew.Soldiers.All(s => s.Health == HealthStatus.Healthy),
                Scenario.EnemyBreakthroughs == 0,
                battery.HitRate, Profile.MissionsCompleted + 1,
                sectorResolution.RewardModifier);
            Budget.Earn(reward.TotalReward, "Mission completion");
            Profile.MissionsCompleted++;
            Profile.TotalKills += battery.ConfirmedKills;
            Profile.EvaluateRankPromotion();
            Crew.RecoverAfterMission(outcome != ScenarioManager.MissionOutcome.Defeat);
            RefreshCrewDisplay();
            ProcessPendingDeliveries();
            RefreshRequisitionState();
            RefreshSectorStateDisplay();
            SaveCampaignState();
            SetStatus($"{outcomeStr}: {reason} | +{reward.TotalReward} OB");
            LogOps("SECTOR", sectorResolution.AfterActionSummary);
            LogOps("REWARD", $"Mission payout credited: {reward.TotalReward} OB ({sectorResolution.RewardModifier:+#;-#;0} objective modifier). Rank now {Profile.Rank}.");
        }
    }

    // ── Utilities ─────────────────────────────────────────────────────

    private void SetStatus(string msg) => StatusMessage = msg;

    private void RefreshFromSimulationSnapshot()
    {
        Sim.RefreshSnapshot();
        UpdateFromSnapshot(Sim.LatestSnapshot);
    }

    private void FlushPendingRadioTraffic()
    {
        Sim.FlushPendingComms();
    }

    private async Task SendPlayerRadioAsync(RadioChannel channel, string content)
    {
        int repliesBefore = GetReplyCount(channel);
        bool aiReady = IsAiReady();
        ActiveChannel = channel;
        var msg = Sim.PlayerSendMessage(channel, content, GetDefaultRecipientCallsign(channel));
        msg.IsRead = true;
        MarkVisibleCommsAsRead();
        Audio.Play(SoundEvent.RadioSquelchOpen);
        SetStatus($"{channel.ToString().ToUpper()}: {content}");
        ApplyManualMessageConsequences(channel, content);
        GameSessionLogger.Current?.OnPlayerAction($"RADIO [{channel}]", content);

        if (AI != null && aiReady)
        {
            try
            {
                GameLogger.Info("UI", $"Sending player message to AI on {channel}: {content.Substring(0, Math.Min(50, content.Length))}...");
                await AI.HandlePlayerMessageAsync(channel, msg.Content, Sim.LatestSnapshot);
                GameLogger.Info("UI", $"AI response completed for {channel}");
            }
            catch (Exception ex)
            {
                GameLogger.Error("UI", $"AI HandlePlayerMessageAsync failed on {channel}", ex);
                SetStatus($"AI ERROR ON {channel.ToString().ToUpper()} - CHECK LOGS");
                // Don't queue generic error messages - let the AI system handle responses
            }
        }

        if (channel == RadioChannel.BatteryNet)
            await HandleBatteryNetMessageAsync(content);

        FlushPendingRadioTraffic();

        if (!aiReady)
        {
            SetStatus($"RADIO SENT ON {channel.ToString().ToUpper()} // AI REPLIES OFFLINE");
        }
        else if (GetReplyCount(channel) == repliesBefore)
        {
            SetStatus($"NO LM STUDIO REPLY ON {channel.ToString().ToUpper()}");
        }
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
        "awacs" => (RadioChannel.CommandNet,
            "ECHO, ALPHA. REQUEST AWACS PICTURE SUPPORT. NEED WIDE-AREA RAID SORT ON CURRENT AXIS."),
        "cap" => (RadioChannel.CommandNet,
            $"ECHO, ALPHA. REQUEST CAP DIVERT. {SelectedTrackObjectiveText.Replace("OBJECTIVE: ", string.Empty)} NOW REQUIRES OUTER-SCREEN COVER."),
        "jam" => (RadioChannel.CommandNet,
            $"ECHO, ALPHA. REQUEST JAM SUPPORT. {(SelectedTrackId ?? "LEAD PACKAGE")} SHOWING COUNTERMEASURE OR ECM ACTIVITY."),
        "battery" => (RadioChannel.AirDefenseNet,
            SelectedTrack != null
                ? $"BRAVO, ALPHA. REQUEST BATTERY SUPPORT ON {SelectedTrackId}. {BuildTrackBraa(SelectedTrack)}."
                : "BRAVO, ALPHA. REQUEST CROSSFIRE LANE ON INNER-RING APPROACH."),
        "intel" => (RadioChannel.IntelNet,
            SelectedTrack != null
                ? $"INTEL, ALPHA. ASSESS {SelectedTrackId}. {BuildTrackBraa(SelectedTrack)}."
                : "INTEL, ALPHA. ASSESS HIGHEST THREAT CONTACT."),
        _ => (ActiveChannel, string.Empty)
    };

    private void RefreshSelectedTrackReadout()
    {
        if (SelectedTrack == null)
        {
            SelectedTrackBraa = "BRAA ---";
            SelectedTrackThreatText = "NO TARGET SELECTED";
            SelectedTrackIdentityText = "IDENTITY: NONE";
            SelectedTrackPackageText = "PACKAGE: NONE";
            SelectedTrackRoleText = "ROLE: STANDBY";
            SelectedTrackObjectiveText = "OBJECTIVE: STANDBY";
            SelectedTrackDoctrineText = "DOCTRINE: NO TRACK SELECTED.";
            SelectedTrackStatusText = "STATUS: STANDBY";
            SelectedTrackMissionText = "MISSION: ---";
            SelectedTrackControlText = "CONTROL: ---";
            SelectedTrackKinematicsText = "KINEMATICS: ---";
            SelectedTrackTimeToThreatText = "TIME TO THREAT: ---";
            SelectedTrackEnvelopeText = "FIRE CONTROL: STANDBY";
            return;
        }

        var advisory = ContactAdvisor.Build(SelectedTrack);
        var doctrine = PackageDoctrineAdvisor.Build(Scenario.CurrentScenario, SelectedTrack);
        var pictureContext = OperationalPicture.SelectedTrack;
        var targetAircraft = SelectedTrack.EntityId == null
            ? null
            : CurrentSnapshot?.HostileAircraft.FirstOrDefault(aircraft => aircraft.Id == SelectedTrack.EntityId);
        string packageLabel = !string.IsNullOrWhiteSpace(targetAircraft?.GroupId)
            ? targetAircraft!.GroupId!.ToUpperInvariant()
            : TrimPrefix(doctrine.PackageLabel);
        string roleLabel = !string.IsNullOrWhiteSpace(targetAircraft?.PackageRoleLabel)
            ? targetAircraft!.PackageRoleLabel!.ToUpperInvariant()
            : TrimPrefix(doctrine.RoleLabel);
        string objectiveLabel = !string.IsNullOrWhiteSpace(targetAircraft?.MissionObjectiveName)
            ? targetAircraft!.MissionObjectiveName!.ToUpperInvariant()
            : TrimPrefix(doctrine.ObjectiveLabel);
        string entryLabel = !string.IsNullOrWhiteSpace(targetAircraft?.EntryLabel)
            ? targetAircraft!.EntryLabel!.ToUpperInvariant()
            : TrimPrefix(doctrine.RouteLabel);
        SelectedTrackBraa = BuildTrackBraa(SelectedTrack);
        SelectedTrackThreatText = $"{advisory.Callout} | {BuildThreatBand(SelectedTrack.ThreatLevel)}";
        SelectedTrackIdentityText = $"IDENTITY: {advisory.IdentityLabel}";
        SelectedTrackPackageText = $"PACKAGE: {packageLabel}";
        SelectedTrackRoleText = $"ROLE: {roleLabel}";
        SelectedTrackObjectiveText = $"OBJECTIVE: {objectiveLabel}";
        SelectedTrackDoctrineText = pictureContext?.DoctrineSummary ?? doctrine.DoctrineLabel;
        var fireControl = AssessFireControlState(Sim.LatestSnapshot.Battery, SelectedTrack);
        var threatState = CurrentSnapshot?.TrackThreatStates.FirstOrDefault(state => state.TrackId == SelectedTrack.TrackId);
        string countermeasureState = targetAircraft == null
            ? "CM CLEAN"
            : targetAircraft.IsChaffActive || targetAircraft.IsFlareActive || targetAircraft.ECMActive
                ? $"CM ACTIVE {(targetAircraft.IsChaffActive ? $"CHAFF({targetAircraft.ChaffCharges}) " : string.Empty)}{(targetAircraft.IsFlareActive ? $"FLARE({targetAircraft.FlareCharges}) " : string.Empty)}{(targetAircraft.ECMActive ? "ECM" : string.Empty)}".Trim()
                : $"CM CLEAN (C:{targetAircraft.ChaffCharges} F:{targetAircraft.FlareCharges})";
        SelectedTrackStatusText = $"STATUS: {advisory.StateLabel} | {advisory.TagsText} | {(pictureContext?.RecommendedAction ?? threatState?.Summary ?? "SEARCH PICTURE")}";
        SelectedTrackMissionText = $"MISSION: {packageLabel} | {roleLabel} | {objectiveLabel} | AXIS {entryLabel}";
        SelectedTrackControlText = $"CONTROL: {BuildTrackRetentionLabel(SelectedTrack)} | {fireControl.FireStatusText} | {countermeasureState} | {AbortAvailabilityText.Replace("ABORT: ", string.Empty)} | {(pictureContext?.CommandCaveats ?? string.Empty)}".TrimEnd();
        SelectedTrackKinematicsText = $"KINEMATICS: ALT {SelectedTrack.AltitudeFt * 0.3048 / 1000:0.0}km | SPD {SelectedTrack.SpeedKts * 1.852:0}km/h | UNC {SelectedTrack.PositionUncertaintyM / 1000:0.0}km | {(CurrentSnapshot?.SelectedWeapon?.ShortCode ?? "9M38")}";
        SelectedTrackTimeToThreatText = advisory.TimeToThreatText;
        SelectedTrackEnvelopeText = $"FIRE CONTROL: {fireControl.FireStatusText} | {fireControl.LockStatusText} | {fireControl.RangeStatusText} | {(pictureContext?.WeaponGating ?? "NO SHOT DATA")}";
    }

    private void RefreshCommandStates()
    {
        StartMissionCommand.NotifyCanExecuteChanged();
        PauseMissionCommand.NotifyCanExecuteChanged();
        DesignateSelectedCommand.NotifyCanExecuteChanged();
        ToggleTrackHoldSelectedCommand.NotifyCanExecuteChanged();
        ReleaseSelectedTrackCommand.NotifyCanExecuteChanged();
        FireSingleCommand.NotifyCanExecuteChanged();
        FireSalvoCommand.NotifyCanExecuteChanged();
        SendQuickCommandCommand.NotifyCanExecuteChanged();
        PurchaseUpgradeCommand.NotifyCanExecuteChanged();
        RefreshReplyStates();
    }

    private void RefreshUnreadCounts()
    {
        CommandUnreadCount = Sim.Comms.UnreadCount(RadioChannel.CommandNet);
        BatteryUnreadCount = Sim.Comms.UnreadCount(RadioChannel.BatteryNet);
        AirDefenseUnreadCount = Sim.Comms.UnreadCount(RadioChannel.AirDefenseNet);
        IntelUnreadCount = Sim.Comms.UnreadCount(RadioChannel.IntelNet);
        GuardUnreadCount = Sim.Comms.UnreadCount(RadioChannel.Guard);
        OpenUnreadCount = Sim.Comms.UnreadCount(RadioChannel.OpenFreq);
        OnPropertyChanged(nameof(CommandChannelLabel));
        OnPropertyChanged(nameof(BatteryChannelLabel));
        OnPropertyChanged(nameof(AirDefenseChannelLabel));
        OnPropertyChanged(nameof(IntelChannelLabel));
        OnPropertyChanged(nameof(GuardChannelLabel));
        OnPropertyChanged(nameof(OpenChannelLabel));
        OnPropertyChanged(nameof(HasUnreadComms));
        OnPropertyChanged(nameof(CommsUnreadBadgeText));
        OnPropertyChanged(nameof(RadioUnreadCount));
        OnPropertyChanged(nameof(HasUnreadRadioTab));
        OnPropertyChanged(nameof(RadioUnreadBadgeText));
    }

    private void RefreshDerivedBindings()
    {
        OnPropertyChanged(nameof(ThreatSummaryText));
        OnPropertyChanged(nameof(MenuButtonText));
        OnPropertyChanged(nameof(SelectedTrackPanelTitle));
        OnPropertyChanged(nameof(EngagementActionHintText));
        OnPropertyChanged(nameof(TrackHoldButtonText));
        OnPropertyChanged(nameof(HasSelectedTrack));
        OnPropertyChanged(nameof(TrackSelectionPromptText));
        OnPropertyChanged(nameof(TargetLockStatusText));
        OnPropertyChanged(nameof(FireControlStatusText));
        OnPropertyChanged(nameof(RangeEnvelopeStatusText));
        OnPropertyChanged(nameof(MissileFlightStatusText));
        OnPropertyChanged(nameof(BatteryLaunchStatusText));
        OnPropertyChanged(nameof(FireControlHeadlineText));
        OnPropertyChanged(nameof(FireControlSummaryText));
        OnPropertyChanged(nameof(FireControlDetailText));
        OnPropertyChanged(nameof(FireControlSupportText));
        OnPropertyChanged(nameof(MissionPrimaryActionText));
        OnPropertyChanged(nameof(MissionPauseHintText));
        OnPropertyChanged(nameof(OperationModeText));
        OnPropertyChanged(nameof(RealisticModeStatusText));
        OnPropertyChanged(nameof(CurrentScenarioArchetypeText));
        OnPropertyChanged(nameof(CurrentScenarioSaveTruthText));
        OnPropertyChanged(nameof(BudgetDisplayText));
        OnPropertyChanged(nameof(PlayerRankDisplayText));
        OnPropertyChanged(nameof(CareerSummaryText));
        OnPropertyChanged(nameof(OwnedUpgradeSummaryText));
        OnPropertyChanged(nameof(StoreAvailabilityText));
        OnPropertyChanged(nameof(CampaignSavePathText));
        OnPropertyChanged(nameof(CrewConditionSummaryText));
        OnPropertyChanged(nameof(CampaignTheaterText));
        OnPropertyChanged(nameof(CurrentScenarioDefinition));
        OnPropertyChanged(nameof(CurrentChannelMessages));
        OnPropertyChanged(nameof(SupportStatusBoard));
        OnPropertyChanged(nameof(SupportActivitySummaryText));
        OnPropertyChanged(nameof(VisibleSupportPictureText));
        OnPropertyChanged(nameof(WeaponLoadoutSummaryText));
        OnPropertyChanged(nameof(SelectedWeaponDisplayText));
        OnPropertyChanged(nameof(SelectedWeaponGuidanceText));
        OnPropertyChanged(nameof(SelectedWeaponEnvelopeText));
        OnPropertyChanged(nameof(SelectedWeaponCountermeasureText));
        OnPropertyChanged(nameof(SelectedWeaponSupportText));
        OnPropertyChanged(nameof(SelectedWeaponDescriptionText));
        OnPropertyChanged(nameof(OperationalPictureText));
        OnPropertyChanged(nameof(RecentIncidentSummaryText));
        OnPropertyChanged(nameof(VisibleFriendlyForceText));
        OnPropertyChanged(nameof(AbortAvailabilityText));
        OnPropertyChanged(nameof(ScenarioContractStatusText));
        OnPropertyChanged(nameof(RadioRulesSummaryText));
        OnPropertyChanged(nameof(HasUnreadComms));
        OnPropertyChanged(nameof(CommsUnreadBadgeText));
    }

    private void RefreshLiveBindings()
    {
        OnPropertyChanged(nameof(ThreatSummaryText));
        OnPropertyChanged(nameof(EngagementActionHintText));
        OnPropertyChanged(nameof(TrackHoldButtonText));
        OnPropertyChanged(nameof(HasSelectedTrack));
        OnPropertyChanged(nameof(TrackSelectionPromptText));
        OnPropertyChanged(nameof(TargetLockStatusText));
        OnPropertyChanged(nameof(FireControlStatusText));
        OnPropertyChanged(nameof(RangeEnvelopeStatusText));
        OnPropertyChanged(nameof(MissileFlightStatusText));
        OnPropertyChanged(nameof(BatteryLaunchStatusText));
        OnPropertyChanged(nameof(FireControlHeadlineText));
        OnPropertyChanged(nameof(FireControlSummaryText));
        OnPropertyChanged(nameof(FireControlDetailText));
        OnPropertyChanged(nameof(FireControlSupportText));
        OnPropertyChanged(nameof(CurrentScenarioArchetypeText));
        OnPropertyChanged(nameof(CurrentScenarioSaveTruthText));
        OnPropertyChanged(nameof(SupportActivitySummaryText));
        OnPropertyChanged(nameof(WeaponLoadoutSummaryText));
        OnPropertyChanged(nameof(SelectedWeaponDisplayText));
        OnPropertyChanged(nameof(SelectedWeaponGuidanceText));
        OnPropertyChanged(nameof(SelectedWeaponEnvelopeText));
        OnPropertyChanged(nameof(SelectedWeaponCountermeasureText));
        OnPropertyChanged(nameof(SelectedWeaponSupportText));
        OnPropertyChanged(nameof(SelectedWeaponDescriptionText));
        OnPropertyChanged(nameof(OperationalPictureText));
        OnPropertyChanged(nameof(RecentIncidentSummaryText));
        OnPropertyChanged(nameof(VisibleFriendlyForceText));
        OnPropertyChanged(nameof(AbortAvailabilityText));
    }

    private void ResetUiForScenarioLoad()
    {
        CommandNetMessages.Clear();
        BatteryNetMessages.Clear();
        AirDefenseNetMessages.Clear();
        IntelNetMessages.Clear();
        OpenFrequencyMessages.Clear();
        GuardMessages.Clear();
        AllMessagesRecent.Clear();
        PriorityOverlayMessages.Clear();
        _supportReportDueSec.Clear();
        _supportVisiblePackages.Clear();
        LogUnreadCount = 0;
        ResetNotificationState();
        ThreatBoardTracks.Clear();
        Sim.Comms.MarkAllRead(RadioChannel.CommandNet);
        Sim.Comms.MarkAllRead(RadioChannel.BatteryNet);
        Sim.Comms.MarkAllRead(RadioChannel.AirDefenseNet);
        Sim.Comms.MarkAllRead(RadioChannel.IntelNet);
        Sim.Comms.MarkAllRead(RadioChannel.Guard);
        Sim.Comms.MarkAllRead(RadioChannel.OpenFreq);
        SelectedTrackId = null;
        SelectedTrack = null;
        SelectedMessage = null;
        _shotReadyCueLatched = false;
        _lastFireControlTrackId = null;
        _lastCrewRefreshGameTimeSec = double.NegativeInfinity;
        _lastSupportDisplayRefreshGameTimeSec = double.NegativeInfinity;
        MissionPhaseText = "MISSION PHASE: STANDBY";
        ObjectiveStatusText = "OBJECTIVE: AWAITING BRIEFING";
        RecommendationText = "RECOMMENDATION: MAINTAIN SEARCH PATTERN";
        AirPictureText = "PICTURE: AIRSPACE CLEAN";
        IncomingThreatText = "INCOMING: NONE";
        ObjectiveBoardText = "OBJECTIVES: STANDBY";
        PackageSummaryText = "PACKAGES: STANDBY";
        SectorEventText = "SECTOR EVENT: STANDBY";
        SectorLoreText = "SECTOR LORE: STANDBY";
        SectorIntegrityText = "SECTOR STATUS: NO OBJECTIVE STATE AVAILABLE.";
        AfterActionSummaryText = "AFTER ACTION: NO RECORDED SORTIE.";
        TheaterSupportText = "THEATER SUPPORT: FULL STOCKS AND NETWORK COVERAGE AVAILABLE.";
        RadarPowerText = "POWER ON";
        RadarEmissionText = "RADAR ACTIVE";
        RadarCommsText = "COMMS GREEN";
        RadarRangeStatusText = "RANGE 148km";
        RadarSweepText = "SWEEP 000";
        TrackHoldStatusText = "TRACK HOLD 0";
        OpsFeed.Clear();
        RefreshRequisitionState();
        RefreshSupportDisplay();
        RefreshSelectedTrackReadout();
        RefreshUnreadCounts();
    }

    private void LoadScenarioDefinition(ScenarioDefinition scenario, bool isRealisticMode)
    {
        ResetUiForScenarioLoad();
        // Start a fresh verbose session log for this game
        GameSessionLogger.StartSession();
        GameSessionLogger.Current?.OnScenarioLoaded(scenario, isRealisticMode);
        Scenario.LoadScenario(scenario);
        ApplyOwnedRequisitionsToCurrentBattery();
        EnsureSectorStateForCurrentScenario();
        FriendlySupport.InitializeForScenario(scenario, CurrentSectorState);
        ApplyTheaterSupportAdjustments();
        RefreshSupportDisplay();
        MissionName = scenario.Name;
        IsRealisticModeActive = isRealisticMode;
        if (!isRealisticMode)
            RealisticModeSummary = "DYNAMIC SHIFT: ready to generate a fresh live scenario.";
        RadarCursorReadout = "CURSOR BRAA: ---";
        IsCommandMenuOpen = true;
        SetMissionLifecycle(MissionLifecycleState.Briefing);
        // Don't flush radio traffic here - let it be delivered when mission starts
        RefreshFromSimulationSnapshot();
        SetStatus($"SCENARIO LOADED: {scenario.Name}");
        LogOps("BRIEF", scenario.Description.Length > 0 ? scenario.Description : $"Scenario {scenario.Name} loaded.");
    }

    private string BuildRealisticModeContext()
    {
        var sector = CurrentSectorState;
        string sectorSummary = sector == null
            ? "fresh sector with no prior damage"
            : SectorCampaignDirector.BuildIntegritySummary(sector);
        return $"{CampaignTheaterText}. {sectorSummary}. Budget {Budget.Balance} OB. Rank {Profile.Rank}. Support state {TheaterSupportText}.";
    }

    private void RefreshAssessment(SimulationSnapshot snapshot)
    {
        var assessment = MissionAdvisor.Build(
            snapshot,
            Scenario.CurrentScenario,
            Scenario.EnemyBreakthroughs,
            SelectedTrack,
            Sim.Weapons);

        MissionPhaseText = assessment.MissionPhase;
        ObjectiveStatusText = assessment.ObjectiveStatus;
        RecommendationText = assessment.Recommendation;

        var intel = BattleIntelDirector.Build(Scenario.CurrentScenario, snapshot, Scenario.EnemyBreakthroughs);
        ObjectiveBoardText = intel.ObjectiveBoard;
        PackageSummaryText = intel.PackageSummary;
        SectorEventText = intel.SectorEvent;
        SectorLoreText = intel.SectorLore;
    }

    private void UpdateBattlePicture(SimulationSnapshot snapshot)
    {
        var pictureTracks = snapshot.AllTracks
            .Where(t => t.Classification is TrackClassification.Hostile or TrackClassification.AssumedHostile or TrackClassification.Unknown)
            .OrderByDescending(t => t.ThreatLevel)
            .ToList();

        if (pictureTracks.Count == 0)
        {
            AirPictureText = "PICTURE: AIRSPACE CLEAN";
            IncomingThreatText = "INCOMING: NONE";
            return;
        }

        AirPictureText = $"PICTURE: {HostileCount} HOSTILE / {TrackCount} TRACKS ACTIVE";

        var incoming = pictureTracks
            .Where(t => ContactAdvisor.Build(t).Callout == "VAMPIRE")
            .OrderBy(t => t.TimeToThreatSec)
            .ToList();

        IncomingThreatText = incoming.Count == 0
            ? "INCOMING: NONE"
            : $"INCOMING: {incoming.Count} VAMPIRE | NEAREST {incoming[0].RangeNm:0.0}NM";
    }

    partial void OnSelectedTrackIdChanged(string? value)
    {
        RefreshWeaponPresentation();
        RefreshDerivedBindings();
        RefreshCommandStates();
    }

    partial void OnSelectedTrackChanged(TrackFile? value)
    {
        RefreshWeaponPresentation();
        RefreshDerivedBindings();
        RefreshCommandStates();
    }
    partial void OnIsCommandMenuOpenChanged(bool value) => RefreshDerivedBindings();
    partial void OnAiAvailableChanged(bool value)
    {
        RefreshDerivedBindings();
        RefreshCommandStates();
    }
    partial void OnSimulationRunningChanged(bool value)
    {
        RefreshDerivedBindings();
        RefreshRequisitionState();
    }

    private async Task HandleBatteryNetMessageAsync(string content)
    {
        var responder = CrewRadioDirector.SelectResponder(Crew, content);
        string line = "";

        if (AI?.AIAvailable == true)
        {
            line = await AI.CrewAgent.GenerateCrewLine(
                responder.FullName,
                CrewRadioDirector.GetPersonalitySummary(responder),
                $"Player battery-net message: {content}. {MissionPhaseText}. {RecommendationText}. {SelectedTrackControlText}. {SelectedTrackEnvelopeText}. {SupportConsequenceText}. {RecentIncidentSummaryText}.",
                Sim.LatestSnapshot);
        }

        if (string.IsNullOrWhiteSpace(line))
            line = BuildFallbackCrewLine(responder, content);

        Sim.Comms.Queue(CommManager.CreateCrewMessage(responder.FullName, line));
    }

    private bool IsAiReady() => AI != null && AiAvailable;

    private void LogOps(string category, string message)
    {
        if (!VisibleLogCategories.Contains(category))
            return;

        DispatchToUI(() =>
        {
            OpsFeed.Insert(0, new OpsFeedItem(category, message));
            while (OpsFeed.Count > 25)
                OpsFeed.RemoveAt(OpsFeed.Count - 1);

            IncrementLogUnread();
        });
    }

    private static string BuildTrackBraa(TrackFile track) =>
        $"BRAA {track.BearingDeg:000}/{track.RangeNm:0.0} ANGELS {track.AltitudeFt / 1000:0.0} {track.AspectString.ToUpper()}";

    private static string BuildTrackRetentionLabel(TrackFile track) =>
        track.IsDesignated ? "STT LOCK" :
        track.IsTrackHeld ? "TWS HOLD" :
        track.IsBeingEngaged ? "MISSILE SUPPORT" :
        track.Quality == TrackQuality.Lost ? "TRACK COAST" :
        "SEARCH TRACK";

    private static string TrimPrefix(string text, string prefix = "")
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            int separator = text.IndexOf(':');
            return separator >= 0 && separator + 1 < text.Length
                ? text[(separator + 1)..].Trim()
                : text.Trim();
        }

        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? text[prefix.Length..].Trim()
            : text.Trim();
    }

    private static string BuildThreatBand(double threatLevel) => threatLevel switch
    {
        >= 0.7 => "HIGH THREAT",
        >= 0.4 => "MEDIUM THREAT",
        _ => "LOW THREAT"
    };

    private static string BuildWeatherSummary(SimulationSnapshot snapshot) =>
        $"WX {snapshot.Weather.Description.ToUpper()} | VIS {snapshot.Weather.VisibilityNm * 1.852:0}km | CEIL {snapshot.Weather.CloudCeilingFt * 0.3048:0}m";

    private string BuildFallbackCrewLine(Soldier responder, string content)
    {
        string normalized = content.ToLowerInvariant();
        return responder.Role switch
        {
            SoldierRole.RadarOperator when normalized.Contains("picture", StringComparison.Ordinal) || normalized.Contains("track", StringComparison.Ordinal)
                => $"Alpha copies. {AirPictureText}. {SelectedTrackBraa}.",
            SoldierRole.FireControl when normalized.Contains("fire", StringComparison.Ordinal) || normalized.Contains("engage", StringComparison.Ordinal) || normalized.Contains("designate", StringComparison.Ordinal)
                => $"Fire control copies. {SelectedTrackEnvelopeText}. {AbortAvailabilityText}.",
            SoldierRole.LauncherChief when normalized.Contains("launcher", StringComparison.Ordinal) || normalized.Contains("missile", StringComparison.Ordinal) || normalized.Contains("reload", StringComparison.Ordinal)
                => $"Launchers report {ReadyLaunchers} ready and {ReserveMissiles} reserve. {SelectedWeaponReadinessText}.",
            SoldierRole.Signals when normalized.Contains("radio", StringComparison.Ordinal) || normalized.Contains("net", StringComparison.Ordinal) || normalized.Contains("support", StringComparison.Ordinal)
                => $"Signals copies. {SupportConsequenceText} {CommandConfidenceText}.",
            _ => $"Command copies. {RecommendationText}. {SelectedTrackControlText}."
        };
    }

    private static void DispatchToUI(Action action)
    {
        GameLogger.Debug("UI-DISPATCH", $"DispatchToUI called on thread {Environment.CurrentManagedThreadId}");
        
        if (Application.Current?.Dispatcher == null)
        {
            GameLogger.Warning("UI-DISPATCH", "Application.Current.Dispatcher is null, cannot dispatch");
            return;
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            GameLogger.Debug("UI-DISPATCH", "Already on UI thread, executing directly");
            try
            {
                action();
            }
            catch (Exception ex)
            {
                GameLogger.Error("UI-DISPATCH", $"Error executing action on UI thread: {ex.Message}");
            }
        }
        else
        {
            GameLogger.Debug("UI-DISPATCH", "Not on UI thread, dispatching async with Normal priority");
            // CRITICAL FIX: Use BeginInvoke (async) instead of Invoke (blocking) to prevent deadlocks
            Application.Current.Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        }
    }

    private ObservableCollection<RadioMessage> GetChannelMessages(RadioChannel channel) => channel switch
    {
        RadioChannel.CommandNet => CommandNetMessages,
        RadioChannel.BatteryNet => BatteryNetMessages,
        RadioChannel.AirDefenseNet => AirDefenseNetMessages,
        RadioChannel.IntelNet => IntelNetMessages,
        RadioChannel.Guard => GuardMessages,
        _ => OpenFrequencyMessages
    };

    private int GetReplyCount(RadioChannel channel) =>
        Sim.Comms.GetHistory(channel).Count(message => !message.IsFromPlayer);

    private static string GetDefaultRecipientCallsign(RadioChannel channel) => channel switch
    {
        RadioChannel.CommandNet => "ECHO ACTUAL",
        RadioChannel.BatteryNet => "ALPHA FIRE UNIT",
        RadioChannel.AirDefenseNet => "BRAVO ACTUAL",
        RadioChannel.IntelNet => "INTEL-1",
        RadioChannel.Guard => "GUARD NET",
        _ => "OPEN NET"
    };

    private void ApplyManualMessageConsequences(RadioChannel channel, string content)
    {
        if (!SimulationRunning)
            return;

        if (channel is not RadioChannel.CommandNet and not RadioChannel.IntelNet and not RadioChannel.AirDefenseNet)
            return;

        string normalized = content.ToLowerInvariant();
        bool cautionTraffic = normalized.Contains("friendly", StringComparison.Ordinal) ||
                              normalized.Contains("civilian", StringComparison.Ordinal) ||
                              normalized.Contains("hold fire", StringComparison.Ordinal) ||
                              normalized.Contains("cease fire", StringComparison.Ordinal);
        bool rescueTraffic = normalized.Contains("mayday", StringComparison.Ordinal) ||
                             normalized.Contains("rescue", StringComparison.Ordinal) ||
                             normalized.Contains("downed", StringComparison.Ordinal) ||
                             normalized.Contains("angel", StringComparison.Ordinal);
        bool blueOnBlueTraffic = normalized.Contains("blue on blue", StringComparison.Ordinal) ||
                                 normalized.Contains("friendly hit", StringComparison.Ordinal) ||
                                 normalized.Contains("fratricide", StringComparison.Ordinal);
        bool releaseLockTraffic = normalized.Contains("release lock", StringComparison.Ordinal) ||
                                  normalized.Contains("drop lock", StringComparison.Ordinal) ||
                                  normalized.Contains("break lock", StringComparison.Ordinal);
        bool divertTraffic = normalized.Contains("divert", StringComparison.Ordinal) ||
                             normalized.Contains("intercept", StringComparison.Ordinal) ||
                             normalized.Contains("scramble", StringComparison.Ordinal) ||
                             normalized.Contains("vector fighters", StringComparison.Ordinal);
        bool weaponsFreeTraffic = normalized.Contains("weapons free", StringComparison.Ordinal);
        bool weaponsHoldTraffic = normalized.Contains("weapons hold", StringComparison.Ordinal);
        bool weaponsTightTraffic = normalized.Contains("weapons tight", StringComparison.Ordinal);
        string? supportType = normalized switch
        {
            _ when normalized.Contains("declare", StringComparison.Ordinal) => "declare",
            _ when normalized.Contains("picture", StringComparison.Ordinal) => "picture",
            _ when normalized.Contains("awacs", StringComparison.Ordinal) => "awacs",
            _ when normalized.Contains("cap", StringComparison.Ordinal) => "cap",
            _ when normalized.Contains("jam", StringComparison.Ordinal) => "jam",
            _ when normalized.Contains("relay", StringComparison.Ordinal) => "relay",
            _ when normalized.Contains("battery", StringComparison.Ordinal) => "battery",
            _ => null
        };

        if (rescueTraffic)
            supportType ??= "sar";
        if (divertTraffic)
            supportType ??= "cap";
        if (normalized.Contains("crossfire", StringComparison.Ordinal) || normalized.Contains("battery support", StringComparison.Ordinal))
            supportType ??= "battery";
        if (blueOnBlueTraffic)
            supportType ??= "sar";

        if (supportType != null)
            IssueSupportRequest(supportType);

        if (releaseLockTraffic && SelectedTrack != null)
        {
            int aborted = Sim.PlayerAbortTrack(SelectedTrack.TrackId);
            Sim.PlayerReleaseTrack(SelectedTrack.TrackId);
            Sim.Weapons.RecordOperationalIncident(
                "lock_release_order",
                aborted > 0
                    ? $"Lock release ordered on {SelectedTrack.TrackId} with abort command in support."
                    : $"Lock release ordered on {SelectedTrack.TrackId}.",
                IncidentSeverity.Warning,
                SelectedTrack.TrackId,
                SelectedTrack.EntityId);
            RefreshFromSimulationSnapshot();
        }

        if (weaponsFreeTraffic && Sim.LatestSnapshot.Battery?.ROE != RulesOfEngagement.WeaponsFree)
        {
            Sim.SetROE(RulesOfEngagement.WeaponsFree, "ECHO ACTUAL AUTHORIZED WEAPONS FREE");
            Sim.Comms.Queue(CommManager.CreateAlliedHQMessage(
                "ALPHA, ECHO. WEAPONS FREE APPROVED. MAINTAIN POSITIVE ID ON ALL FRIENDLY CORRIDORS.",
                MessagePriority.Priority));
            Sim.Weapons.RecordOperationalIncident(
                "roe_shift",
                "Weapons free authorized over command net.",
                IncidentSeverity.Info,
                SelectedTrack?.TrackId,
                SelectedTrack?.EntityId);
        }

        if (weaponsHoldTraffic && Sim.LatestSnapshot.Battery?.ROE != RulesOfEngagement.WeaponsHold)
        {
            if (SelectedTrack != null)
                Sim.PlayerAbortTrack(SelectedTrack.TrackId);
            Sim.SetROE(RulesOfEngagement.WeaponsHold, "ECHO ACTUAL ORDERED WEAPONS HOLD");
            Sim.Weapons.RecordOperationalIncident(
                "weapons_hold_order",
                "Weapons hold ordered over command net.",
                IncidentSeverity.Warning,
                SelectedTrack?.TrackId,
                SelectedTrack?.EntityId);
            Sim.Comms.Queue(CommManager.CreateAlliedHQMessage(
                "ALPHA, ECHO. WEAPONS HOLD. STABILIZE THE PICTURE AND REPORT ANY BREAKTHROUGH IMMEDIATELY.",
                MessagePriority.Immediate));
        }

        if (weaponsTightTraffic && Sim.LatestSnapshot.Battery?.ROE != RulesOfEngagement.WeaponsTight)
        {
            Sim.SetROE(RulesOfEngagement.WeaponsTight, "ECHO ACTUAL ORDERED WEAPONS TIGHT");
            Sim.Comms.Queue(CommManager.CreateAlliedHQMessage(
                "ALPHA, ECHO. WEAPONS TIGHT. DECLARE AND PID REQUIRED BEFORE SHOT AUTHORITY.",
                MessagePriority.Priority));
            Sim.Weapons.RecordOperationalIncident(
                "roe_shift",
                "Weapons tight ordered over command net.",
                IncidentSeverity.Info,
                SelectedTrack?.TrackId,
                SelectedTrack?.EntityId);
        }

        if (blueOnBlueTraffic)
        {
            if (SelectedTrack != null)
                Sim.PlayerAbortTrack(SelectedTrack.TrackId);
            Sim.SetROE(RulesOfEngagement.WeaponsHold, "ECHO ACTUAL BLUE-ON-BLUE PREVENTION");
            Sim.Weapons.RecordOperationalIncident(
                "blue_on_blue_warning",
                "Blue-on-blue warning transmitted on command network. Support posture tightened.",
                IncidentSeverity.Critical,
                SelectedTrack?.TrackId,
                SelectedTrack?.EntityId);
            Sim.Comms.Queue(CommManager.CreateAlliedHQMessage(
                "ALPHA, ECHO. BLUE-ON-BLUE WARNING COPIED. CHECK FIRE, HOLD WEAPONS, AND PREPARE RESCUE/DECONFLICTION CHANNELS.",
                MessagePriority.Immediate));
        }

        if (cautionTraffic)
        {
            if (SelectedTrack != null &&
                SelectedTrack.Classification is TrackClassification.Friendly or TrackClassification.Civilian)
            {
                Sim.PlayerReleaseTrack(SelectedTrack.TrackId);
                RefreshFromSimulationSnapshot();
            }

            if (Sim.LatestSnapshot.Battery?.ROE == RulesOfEngagement.WeaponsFree)
                Sim.SetROE(RulesOfEngagement.WeaponsTight, "ECHO ACTUAL SAFETY CHECK");

            Sim.Weapons.RecordOperationalIncident(
                "check_fire_order",
                "Check-fire traffic tightened engagement discipline.",
                IncidentSeverity.Warning,
                SelectedTrack?.TrackId,
                SelectedTrack?.EntityId);
            Sim.Comms.Queue(CommManager.CreateAlliedHQMessage(
                "ALPHA, ECHO. CHECK FIRE ACKNOWLEDGED. MAINTAIN POSITIVE ID AND REPORT ANY FRIENDLY CONFLICT.",
                MessagePriority.Immediate));
            FlushPendingRadioTraffic();
        }

        Sim.RefreshSnapshot();
        RefreshFromSimulationSnapshot();
    }

    // -- Disposal --------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unsubscribe from all events to prevent memory leaks
        if (_onSimTickHandler != null)
            Sim.Events.Unsubscribe(_onSimTickHandler);
        if (_onNewContactHandler != null)
            Sim.Events.Unsubscribe(_onNewContactHandler);
        if (_onEngagementResultHandler != null)
            Sim.Events.Unsubscribe(_onEngagementResultHandler);
        if (_onMissileLaunchedHandler != null)
            Sim.Events.Unsubscribe(_onMissileLaunchedHandler);
        if (_onAlertChangedHandler != null)
            Sim.Events.Unsubscribe(_onAlertChangedHandler);
        if (_onROEChangedHandler != null)
            Sim.Events.Unsubscribe(_onROEChangedHandler);
        if (_onMissionCompletedHandler != null)
            Sim.Events.Unsubscribe(_onMissionCompletedHandler);
        if (_onBatteryDamagedHandler != null)
            Sim.Events.Unsubscribe(_onBatteryDamagedHandler);

        if (_onMessageReceivedHandler != null)
            Sim.Comms.MessageReceived -= _onMessageReceivedHandler;
        if (_onCrewEventHandler != null)
            Crew.CrewEventOccurred -= _onCrewEventHandler;
        if (_onGameEventFiredHandler != null)
            Events.EventFired -= _onGameEventFiredHandler;
        if (_onWaveSpawnedHandler != null)
            Scenario.OnWaveSpawned -= _onWaveSpawnedHandler;
        if (_onMissionCompleteHandler != null)
            Scenario.OnMissionComplete -= _onMissionCompleteHandler;

        // Dispose owned resources
        Sim.Dispose();
    }

}

// Child ViewModels


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
    private static readonly Brush HostileBrush = CreateFrozenBrush("#FF5050");
    private static readonly Brush FriendlyBrush = CreateFrozenBrush("#3296FF");
    private static readonly Brush UnknownBrush = CreateFrozenBrush("#FFFF50");

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
    [ObservableProperty] private string _packageLabel = "UNATTRIBUTED";
    [ObservableProperty] private bool _isBeingEngaged;
    [ObservableProperty] private bool _hasNoIff;
    [ObservableProperty] private string _qualityLabel = "FIRM";
    
    // Expose TrackFile for converters that need full track data
    [ObservableProperty] private TrackFile? _trackFile;

    public TrackRowViewModel(TrackFile track) { Update(track); }

    public void Update(TrackFile track)
    {
        TrackFile = track; // Store reference for converters
        TrackId = track.TrackId;
        Designation = track.TrackDesignation;
        Classification = track.Classification.ToString().ToUpper();
        Bearing = $"{track.BearingDeg:000}";
        RangeText = $"{track.RangeNm * 1.852:F0}km";
        AltitudeText = $"{track.AltitudeFt * 0.3048 / 1000:F1}km";
        SpeedText = $"{track.SpeedKts * 1.852:F0}";
        ThreatText = track.ThreatLevel > 0.7 ? "HIGH" : track.ThreatLevel > 0.4 ? "MED" : "LOW";
        Aspect = track.AspectString;
        PackageLabel = string.IsNullOrWhiteSpace(track.GroupLabel) ? "UNATTRIBUTED" : track.GroupLabel.ToUpperInvariant();
        IsBeingEngaged = track.IsBeingEngaged;
        HasNoIff = track.IFFInterrogated && !track.IFFResponse;
        QualityLabel = track.Quality.ToString().ToUpperInvariant();
        RowColor = track.Classification switch
        {
            TrackClassification.Hostile or TrackClassification.AssumedHostile => "#FF5050",
            TrackClassification.Friendly => "#3296FF",
            _ => "#FFFF50"
        };
        RowBrush = track.Classification switch
        {
            TrackClassification.Hostile or TrackClassification.AssumedHostile => HostileBrush,
            TrackClassification.Friendly => FriendlyBrush,
            _ => UnknownBrush
        };
    }

    private static Brush CreateFrozenBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
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

public sealed class OpsFeedItem
{
    public OpsFeedItem(string category, string message)
    {
        Category = category;
        Message = message;
        Timestamp = DateTime.UtcNow;
    }

    public string Category { get; }
    public string Message { get; }
    public DateTime Timestamp { get; }
    public string TimeText => Timestamp.ToString("HH:mm:ss");
}
