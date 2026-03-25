using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DEADSKY.Core.Campaign;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Scenario;

namespace DEADSKY.App.ViewModels;

public enum CommsDrawerTab
{
    Radio,
    Alerts,
    Log
}

public partial class MainViewModel
{
    [ObservableProperty] private RadioMessage? _selectedMessage;
    [ObservableProperty] private int _airDefenseUnreadCount;
    [ObservableProperty] private int _openUnreadCount;
    [ObservableProperty] private int _guardUnreadCount;
    [ObservableProperty] private int _alertsUnreadCount;
    [ObservableProperty] private int _logUnreadCount;
    [ObservableProperty] private CommsDrawerTab _activeCommsTab = CommsDrawerTab.Radio;

    public ObservableCollection<RadioMessage> AirDefenseNetMessages { get; } = new();
    public ObservableCollection<RadioMessage> OpenFrequencyMessages { get; } = new();
    public ObservableCollection<RadioMessage> GuardMessages { get; } = new();
    public ObservableCollection<RadioMessage> PriorityOverlayMessages { get; } = new();
    public ObservableCollection<SupportPackageViewModel> SupportPackages { get; } = new();

    public ObservableCollection<RadioMessage> CurrentChannelMessages => ActiveChannel switch
    {
        RadioChannel.CommandNet => CommandNetMessages,
        RadioChannel.BatteryNet => BatteryNetMessages,
        RadioChannel.AirDefenseNet => AirDefenseNetMessages,
        RadioChannel.IntelNet => IntelNetMessages,
        RadioChannel.Guard => GuardMessages,
        _ => OpenFrequencyMessages
    };

    public string AirDefenseChannelLabel => AirDefenseUnreadCount > 0 ? $"ADF {AirDefenseUnreadCount}" : "ADF";
    public string OpenChannelLabel => OpenUnreadCount > 0 ? $"OPEN {OpenUnreadCount}" : "OPEN";
    public string GuardChannelLabel => GuardUnreadCount > 0 ? $"GDR {GuardUnreadCount}" : "GDR";
    public int RadioUnreadCount => Sim.Comms.TotalUnread;
    public bool HasUnreadRadioTab => RadioUnreadCount > 0;
    public bool HasUnreadAlertsTab => AlertsUnreadCount > 0;
    public bool HasUnreadLogTab => LogUnreadCount > 0;
    public string RadioUnreadBadgeText => FormatUnreadCount(RadioUnreadCount);
    public string AlertsUnreadBadgeText => FormatUnreadCount(AlertsUnreadCount);
    public string LogUnreadBadgeText => FormatUnreadCount(LogUnreadCount);
    public string PlayerIdentityText => "YOU // LT ALPHA ACTUAL";
    public string ActiveChannelRouteText => ActiveChannel switch
    {
        RadioChannel.CommandNet => "TO // ECHO ACTUAL // SECTOR HQ TASKING",
        RadioChannel.BatteryNet => "TO // ALPHA FIRE UNIT // CREW COORDINATION",
        RadioChannel.AirDefenseNet => "TO // BRAVO ACTUAL // CROSS-BATTERY NET",
        RadioChannel.IntelNet => "TO // INTEL-1 // THREAT ANALYSIS CELL",
        RadioChannel.Guard => "TO // GUARD NET // EMERGENCY TRAFFIC ONLY",
        _ => "TO // OPEN FREQ // UNCONTROLLED BROADCAST"
    };
    public string ActiveChannelEffectText => ActiveChannel switch
    {
        RadioChannel.CommandNet => "Command net reaches ECHO ACTUAL. Picture, declare, and support traffic can task friendly command layers.",
        RadioChannel.BatteryNet => "Battery net talks to your own crew. Expect short crew acknowledgments and internal coordination.",
        RadioChannel.AirDefenseNet => "Air-defense net reaches nearby batteries and support elements for cross-fire and sector coordination.",
        RadioChannel.IntelNet => "Intel net reaches the analysis cell for track assessment, warning, and declare support.",
        RadioChannel.Guard => "Guard is emergency-only. Routine traffic will be redirected or shut down.",
        _ => "Open frequency is uncontrolled. Hostile, civilian, or nobody may answer depending on the situation."
    };
    public string SupportStatusBoard => FriendlySupport.BuildStatusBoard();
    public string VisibleSupportPictureText => SupportPackages.Count == 0
        ? "SUPPORT PICTURE: NO ACTIVE SUPPORT."
        : "SUPPORT PICTURE: " + string.Join(" | ", SupportPackages
            .Where(package => package.IsVisibleInPicture)
            .Select(package => package.PictureLabel)
            .DefaultIfEmpty("NO VISIBLE SUPPORT TRACKS"));
    public string ScenarioContractStatusText => CurrentScenarioDefinition == null
        ? "REALISTIC CONTRACT: STANDBY"
        : ScenarioContractValidator.ValidateScenario(CurrentScenarioDefinition).Summary;
    public string RadioRulesSummaryText => RadioRules.BuildGuidanceSummary();
    public bool CanReadActiveRadioChannel => IsCommsDrawerOpen && ActiveCommsTab == CommsDrawerTab.Radio;

    partial void OnActiveChannelChanged(RadioChannel value)
    {
        MarkVisibleCommsAsRead();
        OnPropertyChanged(nameof(CurrentChannelMessages));
        OnPropertyChanged(nameof(ActiveChannelRouteText));
        OnPropertyChanged(nameof(ActiveChannelEffectText));
        RefreshUnreadCounts();
    }

    partial void OnAlertsUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnreadAlertsTab));
        OnPropertyChanged(nameof(AlertsUnreadBadgeText));
    }

    partial void OnLogUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnreadLogTab));
        OnPropertyChanged(nameof(LogUnreadBadgeText));
    }

    partial void OnActiveCommsTabChanged(CommsDrawerTab value) => MarkVisibleCommsAsRead();

    partial void OnSelectedMessageChanged(RadioMessage? value) => RefreshReplyStates();

    [RelayCommand(CanExecute = nameof(CanReplyToSelectedMessage))]
    private async Task SendWarningReply()
    {
        var target = GetReplyTarget();
        if (target == null)
            return;

        await SendPlayerRadioAsync(target.Channel,
            $"{target.SenderCallsign}, ALPHA. YOU ARE APPROACHING A DEFENDED SECTOR. TURN AWAY IMMEDIATELY OR YOU MAY BE ENGAGED.");
    }

    [RelayCommand(CanExecute = nameof(CanReplyToSelectedMessage))]
    private async Task SendAcknowledgeReply()
    {
        var target = GetReplyTarget();
        if (target == null)
            return;

        await SendPlayerRadioAsync(target.Channel,
            $"{target.SenderCallsign}, ALPHA. ROGER {target.PriorityTag}. MESSAGE RECEIVED.");
    }

    [RelayCommand(CanExecute = nameof(CanReplyToSelectedMessage))]
    private async Task SendHoldFireReply()
    {
        var target = GetReplyTarget();
        if (target == null)
            return;

        await SendPlayerRadioAsync(target.Channel,
            $"{target.SenderCallsign}, ALPHA. HOLD FIRE. IDENTIFY AND MAINTAIN STANDOFF.");
    }

    [RelayCommand(CanExecute = nameof(CanReplyToSelectedMessage))]
    private async Task SendRepeatReply()
    {
        var target = GetReplyTarget();
        if (target == null)
            return;

        await SendPlayerRadioAsync(target.Channel,
            $"{target.SenderCallsign}, ALPHA. SAY AGAIN LAST TRANSMISSION.");
    }

    [RelayCommand(CanExecute = nameof(CanReplyToSelectedMessage))]
    private async Task SendSurrenderReply(string decision)
    {
        var target = GetReplyTarget();
        if (target == null)
            return;

        string content = string.Equals(decision, "accept", StringComparison.OrdinalIgnoreCase)
            ? $"{target.SenderCallsign}, ALPHA. CEASE APPROACH, TURN COLD, DESCEND TO SAFE HEADING, AND STAND BY FOR INSTRUCTIONS."
            : $"{target.SenderCallsign}, ALPHA. NEGATIVE. YOU REMAIN A THREAT. TURN AWAY NOW OR EXPECT ENGAGEMENT.";

        await SendPlayerRadioAsync(target.Channel, content);
    }

    [RelayCommand(CanExecute = nameof(CanIssueSupportRequest))]
    private void IssueSupportRequest(string supportType)
    {
        FriendlySupportType type = ParseSupportType(supportType);
        string cue = supportType.ToUpperInvariant() switch
        {
            "PICTURE" => "Need refreshed raid picture and package sort.",
            "DECLARE" => SelectedTrack != null
                ? $"Need declare on {SelectedTrackId}. {BuildTrackBraa(SelectedTrack)}."
                : "Need hostile declaration on highest-threat contact.",
            "CAP" => "Need combat air patrol diversion to stiffen outer screen.",
            "JAM" => "Need friendly escort-jam to disrupt hostile coordination.",
            "RELAY" => "Need communications relay recovery on strained command net.",
            "BATTERY" => "Need cross-battery fire support lane.",
            "AWACS" => "Need wide-area fused picture and package labels.",
            _ => "Support requested."
        };

        var result = FriendlySupport.RequestSupport(type, "ALPHA ACTUAL", cue, Sim.GameTimeSec);
        SetStatus(result.Accepted ? $"SUPPORT TASKED: {supportType.ToUpperInvariant()}" : $"SUPPORT DENIED: {supportType.ToUpperInvariant()}");
        LogOps("SUPPORT", result.Summary);
        FlushPendingRadioTraffic();
        RefreshSupportDisplay();
    }

    private bool CanIssueSupportRequest(string? supportType) =>
        SimulationRunning && !string.IsNullOrWhiteSpace(supportType);

    internal void RefreshSupportDisplay()
    {
        for (int i = 0; i < FriendlySupport.Packages.Count; i++)
        {
            var package = FriendlySupport.Packages[i];
            if (i < SupportPackages.Count)
                SupportPackages[i].Update(package);
            else
                SupportPackages.Add(new SupportPackageViewModel(package));
        }

        while (SupportPackages.Count > FriendlySupport.Packages.Count)
            SupportPackages.RemoveAt(SupportPackages.Count - 1);

        OnPropertyChanged(nameof(SupportStatusBoard));
        OnPropertyChanged(nameof(VisibleSupportPictureText));
        OnPropertyChanged(nameof(ScenarioContractStatusText));
        OnPropertyChanged(nameof(RadioRulesSummaryText));
    }

    internal void RefreshReplyStates()
    {
        SendWarningReplyCommand.NotifyCanExecuteChanged();
        SendAcknowledgeReplyCommand.NotifyCanExecuteChanged();
        SendHoldFireReplyCommand.NotifyCanExecuteChanged();
        SendRepeatReplyCommand.NotifyCanExecuteChanged();
        SendSurrenderReplyCommand.NotifyCanExecuteChanged();
        IssueSupportRequestCommand.NotifyCanExecuteChanged();
    }

    private bool CanReplyToSelectedMessage() =>
        GetReplyTarget()?.CanReply == true;

    private RadioMessage? GetReplyTarget() =>
        SelectedMessage?.CanReply == true
            ? SelectedMessage
            : CurrentChannelMessages.LastOrDefault(message => message.CanReply && !message.IsFromPlayer);

    internal void SetActiveCommsTab(int selectedIndex)
    {
        ActiveCommsTab = selectedIndex switch
        {
            1 => CommsDrawerTab.Alerts,
            2 => CommsDrawerTab.Log,
            _ => CommsDrawerTab.Radio
        };
    }

    internal void MarkVisibleCommsAsRead()
    {
        if (!IsCommsDrawerOpen)
            return;

        if (ActiveCommsTab == CommsDrawerTab.Radio)
            Sim.Comms.MarkAllRead(ActiveChannel);
        else if (ActiveCommsTab == CommsDrawerTab.Alerts)
            AlertsUnreadCount = 0;
        else
            LogUnreadCount = 0;

        RefreshUnreadCounts();
    }

    internal void IncrementAlertsUnread()
    {
        if (IsCommsDrawerOpen && ActiveCommsTab == CommsDrawerTab.Alerts)
            return;

        AlertsUnreadCount++;
    }

    internal void IncrementLogUnread()
    {
        if (IsCommsDrawerOpen && ActiveCommsTab == CommsDrawerTab.Log)
            return;

        LogUnreadCount++;
    }

    private static FriendlySupportType ParseSupportType(string supportType) => supportType.ToLowerInvariant() switch
    {
        "declare" => FriendlySupportType.DeclarationCell,
        "cap" => FriendlySupportType.CombatAirPatrol,
        "jam" => FriendlySupportType.JammingSupport,
        "relay" => FriendlySupportType.RelayRecovery,
        "battery" => FriendlySupportType.NearbyBattery,
        "awacs" => FriendlySupportType.Awacs,
        "sar" => FriendlySupportType.SearchAndRescue,
        _ => FriendlySupportType.PictureRelay
    };

    private static string FormatUnreadCount(int count) =>
        count > 9 ? "9+" : count.ToString();
}

public partial class SupportPackageViewModel : ObservableObject
{
    private readonly FriendlySupportPackage _package;

    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _typeText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _detailText = "";
    [ObservableProperty] private string _pictureLabel = "";
    [ObservableProperty] private bool _isVisibleInPicture;

    public SupportPackageViewModel(FriendlySupportPackage package)
    {
        _package = package;
        Update(package);
    }

    public void Update(FriendlySupportPackage package)
    {
        DisplayName = $"{package.DisplayName} // {package.UnitCallsign}";
        TypeText = package.Type.ToString().ToUpperInvariant();
        StatusText = package.Availability.ToString().ToUpperInvariant();
        DetailText = package.LastSummary;
        IsVisibleInPicture = package.IsVisibleInPicture;
        PictureLabel = package.IsVisibleInPicture
            ? $"{package.UnitCallsign} ACTIVE"
            : $"{package.UnitCallsign} STANDBY";
    }
}
