using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Scenario;

namespace DEADSKY.Core.Campaign;

public enum FriendlySupportType
{
    PictureRelay,
    DeclarationCell,
    CombatAirPatrol,
    JammingSupport,
    RelayRecovery,
    SearchAndRescue,
    NearbyBattery,
    Awacs
}

public enum SupportAvailabilityState
{
    Ready,
    Tasked,
    CoolingDown,
    Damaged,
    Unavailable
}

public sealed class FriendlySupportPackage
{
    public string Id { get; init; } = "";
    public FriendlySupportType Type { get; init; }
    public string DisplayName { get; init; } = "";
    public string RankOrRole { get; init; } = "";
    public string UnitCallsign { get; init; } = "";
    public string Designation { get; init; } = "";
    public double Reliability { get; init; }
    public double Risk { get; init; }
    public SupportAvailabilityState Availability { get; set; } = SupportAvailabilityState.Ready;
    public double DelayRemainingSec { get; set; }
    public double CooldownRemainingSec { get; set; }
    public double VisibleUntilSec { get; set; }
    public string LastSummary { get; set; } = "Standing by.";

    public bool IsVisibleInPicture => VisibleUntilSec > 0;
    public string StatusLine => $"{DisplayName} // {UnitCallsign} | {Availability.ToString().ToUpper()} | REL {Reliability:P0} | {LastSummary}";
}

public sealed record SupportRequest(
    FriendlySupportType Type,
    string Requestor,
    string Details,
    double RequestedAtSec,
    string? ConversationId = null);

public sealed record SupportRequestResult(
    bool Accepted,
    string Summary,
    string? PackageId,
    double EtaSec,
    bool VisibleSupport);

public sealed class FriendlySupportDirector
{
    private readonly CommManager _comms;
    private readonly List<FriendlySupportPackage> _packages = new();
    private readonly List<SupportRequest> _pendingRequests = new();

    public FriendlySupportDirector(CommManager comms)
    {
        _comms = comms;
    }

    public IReadOnlyList<FriendlySupportPackage> Packages => _packages;

    public void InitializeForScenario(ScenarioDefinition? scenario, SectorCampaignState? sectorState)
    {
        _packages.Clear();

        bool relayDamaged = sectorState?.Objectives.Any(o =>
            o.ObjectiveId.Contains("relay", StringComparison.OrdinalIgnoreCase) &&
            o.IntegrityPct <= 0.5) == true;

        _packages.Add(new FriendlySupportPackage
        {
            Id = "SUP-GCI",
            Type = FriendlySupportType.PictureRelay,
            DisplayName = "SABLE CONTROL",
            RankOrRole = "GCI",
            UnitCallsign = "SABLE-1",
            Designation = "SECTOR PICTURE CELL",
            Reliability = relayDamaged ? 0.62 : 0.88,
            Risk = 0.15,
            Availability = relayDamaged ? SupportAvailabilityState.CoolingDown : SupportAvailabilityState.Ready,
            CooldownRemainingSec = relayDamaged ? 90 : 0,
            LastSummary = relayDamaged ? "Relay strain reported. Picture updates delayed." : "Ready for picture and declare traffic."
        });

        _packages.Add(new FriendlySupportPackage
        {
            Id = "SUP-DECLARE",
            Type = FriendlySupportType.DeclarationCell,
            DisplayName = "ORACLE CELL",
            RankOrRole = "ID",
            UnitCallsign = "ORACLE-4",
            Designation = "DECLARATION AND IFF DESK",
            Reliability = relayDamaged ? 0.58 : 0.84,
            Risk = 0.12,
            Availability = relayDamaged ? SupportAvailabilityState.CoolingDown : SupportAvailabilityState.Ready,
            CooldownRemainingSec = relayDamaged ? 75 : 0,
            LastSummary = relayDamaged ? "IFF desk degraded by relay strain. Declare support delayed." : "Ready for declare and hostile-assessment traffic."
        });

        _packages.Add(new FriendlySupportPackage
        {
            Id = "SUP-CAP",
            Type = FriendlySupportType.CombatAirPatrol,
            DisplayName = "VIPER LEAD",
            RankOrRole = "FLT",
            UnitCallsign = "VIPER 1-1",
            Designation = "DIVERTED CAP",
            Reliability = 0.76,
            Risk = 0.48,
            LastSummary = "Cold on station west of sector. Available for diversion."
        });

        _packages.Add(new FriendlySupportPackage
        {
            Id = "SUP-JAM",
            Type = FriendlySupportType.JammingSupport,
            DisplayName = "MISTRAL",
            RankOrRole = "ECM",
            UnitCallsign = "MISTRAL-2",
            Designation = "STANDOFF JAMMER CELL",
            Reliability = 0.71,
            Risk = 0.37,
            LastSummary = "Standoff jamming orbit available on command tasking."
        });

        _packages.Add(new FriendlySupportPackage
        {
            Id = "SUP-AWACS",
            Type = FriendlySupportType.Awacs,
            DisplayName = "LANTERN",
            RankOrRole = "CRC",
            UnitCallsign = "LANTERN-6",
            Designation = "AIRBORNE EARLY WARNING",
            Reliability = 0.83,
            Risk = 0.22,
            LastSummary = "Wide-area picture coverage available."
        });

        _packages.Add(new FriendlySupportPackage
        {
            Id = "SUP-BRAVO",
            Type = FriendlySupportType.NearbyBattery,
            DisplayName = "BRAVO ACTUAL",
            RankOrRole = "BATTERY",
            UnitCallsign = "BRAVO",
            Designation = "ADJACENT SAM BATTERY",
            Reliability = 0.8,
            Risk = 0.31,
            LastSummary = "Cross-battery fires available on priority call."
        });

        _packages.Add(new FriendlySupportPackage
        {
            Id = "SUP-RELAY",
            Type = FriendlySupportType.RelayRecovery,
            DisplayName = "SWITCHBOARD",
            RankOrRole = "COMMS",
            UnitCallsign = "RELAY-2",
            Designation = "NETWORK RESTORATION TEAM",
            Reliability = 0.74,
            Risk = 0.18,
            LastSummary = "Ready to restore strained network links."
        });

        _packages.Add(new FriendlySupportPackage
        {
            Id = "SUP-SAR",
            Type = FriendlySupportType.SearchAndRescue,
            DisplayName = "ANGEL",
            RankOrRole = "CSAR",
            UnitCallsign = "ANGEL-3",
            Designation = "COMBAT RESCUE DET",
            Reliability = 0.68,
            Risk = 0.52,
            Availability = scenario == null ? SupportAvailabilityState.Unavailable : SupportAvailabilityState.Ready,
            LastSummary = "Standing by for downed-friendly contingencies."
        });
    }

    public void Tick(double deltaTime, double gameTimeSec)
    {
        foreach (var package in _packages)
        {
            if (package.DelayRemainingSec > 0)
            {
                package.DelayRemainingSec = Math.Max(0, package.DelayRemainingSec - deltaTime);
                if (package.DelayRemainingSec == 0 && package.Availability == SupportAvailabilityState.Tasked)
                {
                    bool failed = package.Risk >= 0.5 &&
                        SimulationRandom.Instance.NextDouble() < (package.Risk * 0.35);
                    package.CooldownRemainingSec = GetCooldown(package.Type);
                    package.VisibleUntilSec = failed ? 0 : GetVisibleWindow(package.Type);
                    package.Availability = failed ? SupportAvailabilityState.Damaged : SupportAvailabilityState.CoolingDown;
                    package.LastSummary = failed
                        ? BuildFailureSummary(package.Type, package.UnitCallsign)
                        : BuildCompletionSummary(package.Type, package.UnitCallsign);
                    _comms.Queue(CommManager.CreateMessage(
                        RadioRules.CreateFriendlySupportProfile(package.DisplayName, package.RankOrRole, package.UnitCallsign, package.Designation),
                        ResolveChannel(package.Type),
                        package.LastSummary,
                        MessagePriority.Priority,
                        MessageType.StatusReport,
                        recipient: "ALPHA",
                        canReply: true,
                        staticLevel: 0.12));
                }
            }

            if (package.CooldownRemainingSec > 0)
            {
                package.CooldownRemainingSec = Math.Max(0, package.CooldownRemainingSec - deltaTime);
                if (package.CooldownRemainingSec == 0 &&
                    package.Availability is SupportAvailabilityState.CoolingDown or SupportAvailabilityState.Damaged)
                {
                    package.Availability = SupportAvailabilityState.Ready;
                    package.LastSummary = "Support package reset and ready.";
                }
            }

            if (package.VisibleUntilSec > 0)
                package.VisibleUntilSec = Math.Max(0, package.VisibleUntilSec - deltaTime);
        }

        _pendingRequests.RemoveAll(request => gameTimeSec - request.RequestedAtSec > 900);
    }

    public SupportRequestResult RequestSupport(
        FriendlySupportType type,
        string requestor,
        string details,
        double gameTimeSec)
    {
        var package = _packages
            .Where(p => p.Type == type)
            .OrderByDescending(p => p.Reliability)
            .FirstOrDefault();

        if (package == null)
            return new SupportRequestResult(false, $"No support package mapped for {type}.", null, 0, false);

        if (package.Availability != SupportAvailabilityState.Ready)
        {
            string blocked = package.Availability == SupportAvailabilityState.CoolingDown
                ? $"Busy. Ready in {package.CooldownRemainingSec:0}s."
                : "Unavailable.";
            return new SupportRequestResult(false, $"{package.DisplayName} cannot comply. {blocked}", package.Id, 0, false);
        }

        double eta = GetDelay(type);
        package.Availability = SupportAvailabilityState.Tasked;
        package.DelayRemainingSec = eta;
        package.LastSummary = $"Tasked by {requestor}. {details}";
        _pendingRequests.Add(new SupportRequest(type, requestor, details, gameTimeSec));

        var speaker = RadioRules.CreateFriendlySupportProfile(package.DisplayName, package.RankOrRole, package.UnitCallsign, package.Designation);
        _comms.Queue(CommManager.CreateMessage(
            speaker,
            ResolveChannel(type),
            BuildAcceptanceSummary(package, details, eta),
            MessagePriority.Priority,
            MessageType.StatusReport,
            recipient: "ALPHA",
            canReply: true,
            staticLevel: 0.14));

        return new SupportRequestResult(true, package.LastSummary, package.Id, eta, GetVisibleWindow(type) > 0);
    }

    public string CancelSupport(string packageId)
    {
        var package = _packages.FirstOrDefault(p => p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        if (package == null)
            return "Unknown support package.";

        if (package.Availability != SupportAvailabilityState.Tasked)
            return $"{package.DisplayName} is not currently tasked.";

        package.Availability = SupportAvailabilityState.CoolingDown;
        package.DelayRemainingSec = 0;
        package.CooldownRemainingSec = 45;
        package.LastSummary = "Tasking cancelled. Rebuilding orbit and comms picture.";
        _comms.Queue(CommManager.CreateMessage(
            RadioRules.CreateFriendlySupportProfile(package.DisplayName, package.RankOrRole, package.UnitCallsign, package.Designation),
            ResolveChannel(package.Type),
            package.LastSummary,
            MessagePriority.Routine,
            MessageType.StatusReport,
            recipient: "ALPHA",
            canReply: false,
            staticLevel: 0.1));
        return package.LastSummary;
    }

    public string BuildStatusBoard() =>
        _packages.Count == 0
            ? "FRIENDLY SUPPORT: NO PACKAGE DATA."
            : "FRIENDLY SUPPORT: " + string.Join(" || ", _packages.Select(package => package.StatusLine));

    public IEnumerable<FriendlySupportPackage> GetVisiblePictureAssets() =>
        _packages.Where(package => package.IsVisibleInPicture);

    private static double GetDelay(FriendlySupportType type) => type switch
    {
        FriendlySupportType.PictureRelay => 15,
        FriendlySupportType.DeclarationCell => 20,
        FriendlySupportType.CombatAirPatrol => 70,
        FriendlySupportType.JammingSupport => 55,
        FriendlySupportType.RelayRecovery => 45,
        FriendlySupportType.SearchAndRescue => 90,
        FriendlySupportType.NearbyBattery => 35,
        FriendlySupportType.Awacs => 25,
        _ => 30
    };

    private static double GetCooldown(FriendlySupportType type) => type switch
    {
        FriendlySupportType.PictureRelay => 45,
        FriendlySupportType.DeclarationCell => 40,
        FriendlySupportType.CombatAirPatrol => 180,
        FriendlySupportType.JammingSupport => 160,
        FriendlySupportType.RelayRecovery => 75,
        FriendlySupportType.SearchAndRescue => 220,
        FriendlySupportType.NearbyBattery => 90,
        FriendlySupportType.Awacs => 80,
        _ => 60
    };

    private static double GetVisibleWindow(FriendlySupportType type) => type switch
    {
        FriendlySupportType.CombatAirPatrol => 180,
        FriendlySupportType.JammingSupport => 150,
        FriendlySupportType.Awacs => 240,
        FriendlySupportType.NearbyBattery => 120,
        _ => 0
    };

    private static RadioChannel ResolveChannel(FriendlySupportType type) => type switch
    {
        FriendlySupportType.NearbyBattery => RadioChannel.AirDefenseNet,
        FriendlySupportType.PictureRelay or FriendlySupportType.DeclarationCell or FriendlySupportType.Awacs => RadioChannel.IntelNet,
        _ => RadioChannel.CommandNet
    };

    private static string BuildAcceptanceSummary(FriendlySupportPackage package, string details, double eta) => package.Type switch
    {
        FriendlySupportType.PictureRelay => $"{package.UnitCallsign}, picture relay tasking copied. Refreshing sector picture. Stand by {eta:0} seconds.",
        FriendlySupportType.DeclarationCell => $"{package.UnitCallsign}, declare cell copied. Correlating IFF and prior route library now.",
        FriendlySupportType.CombatAirPatrol => $"{package.UnitCallsign}, CAP diversion approved. Fighters vectoring toward your sector. ETA {eta:0} seconds.",
        FriendlySupportType.JammingSupport => $"{package.UnitCallsign}, escort-jam support turning in. Expect standoff cover shortly.",
        FriendlySupportType.RelayRecovery => $"{package.UnitCallsign}, relay recovery team moving. Working your strained network.",
        FriendlySupportType.NearbyBattery => $"{package.UnitCallsign}, cross-battery fires coordinating. {details}",
        FriendlySupportType.Awacs => $"{package.UnitCallsign}, widening picture support. Stand by for labeled raid picture.",
        _ => $"{package.UnitCallsign}, support request accepted. ETA {eta:0} seconds."
    };

    private static string BuildCompletionSummary(FriendlySupportType type, string unitCallsign) => type switch
    {
        FriendlySupportType.PictureRelay => $"{unitCallsign}, updated picture: raid packages sorted, likely primary striker identified.",
        FriendlySupportType.DeclarationCell => $"{unitCallsign}, declare complete. Highest-confidence hostile tracks flagged in your picture.",
        FriendlySupportType.CombatAirPatrol => $"{unitCallsign}, CAP established west of sector. Friendly fighters now visible on tactical picture.",
        FriendlySupportType.JammingSupport => $"{unitCallsign}, escort-jam active. Hostile raid coordination showing degradation.",
        FriendlySupportType.RelayRecovery => $"{unitCallsign}, network relay restored. Command latency improving.",
        FriendlySupportType.SearchAndRescue => $"{unitCallsign}, rescue det staged. No recovery launch yet.",
        FriendlySupportType.NearbyBattery => $"{unitCallsign}, BRAVO battery holding crossfire lane and reporting ready.",
        FriendlySupportType.Awacs => $"{unitCallsign}, wide-area picture cleanly fused. Support track labels pushed to the board.",
        _ => $"{unitCallsign}, support action complete."
    };

    private static string BuildFailureSummary(FriendlySupportType type, string unitCallsign) => type switch
    {
        FriendlySupportType.CombatAirPatrol => $"{unitCallsign}, CAP diversion forced off by threat fuel or weather. Rebuilding outer screen.",
        FriendlySupportType.JammingSupport => $"{unitCallsign}, jamming run cut short under pressure. Effects partial only.",
        FriendlySupportType.NearbyBattery => $"{unitCallsign}, cross-battery window collapsed. Hold your own fire lane.",
        FriendlySupportType.Awacs => $"{unitCallsign}, fused picture degraded. Relay quality unstable.",
        _ => $"{unitCallsign}, support action degraded in execution."
    };
}
