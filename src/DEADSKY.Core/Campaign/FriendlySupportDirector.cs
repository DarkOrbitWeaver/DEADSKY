using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

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
    public double BaseReliability { get; init; }
    public double BaseRisk { get; init; }
    public double Reliability { get; set; }
    public double Risk { get; set; }
    public SupportAvailabilityState Availability { get; set; } = SupportAvailabilityState.Ready;
    public double DelayRemainingSec { get; set; }
    public double CooldownRemainingSec { get; set; }
    public double VisibleUntilSec { get; set; }
    public string LastSummary { get; set; } = "Standing by.";
    public double BearingDeg { get; set; }
    public double RangeNm { get; set; }
    public double AltitudeFt { get; set; }
    public Vec2 Position => CoordinateSystem.FromBearingRange(BearingDeg, RangeNm);

    // ── Real entity tracking (Task 2.1) ───────────────────────────────
    /// <summary>
    /// Entity ID of spawned CAP fighter (for CombatAirPatrol type).
    /// Null if no entity is currently spawned.
    /// </summary>
    public string? SpawnedEntityId { get; set; }

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
    private readonly EntityManager? _entityManager;
    private readonly RadarSystem? _radarSystem;
    private readonly List<FriendlySupportPackage> _packages = new();
    private readonly List<SupportRequest> _pendingRequests = new();
    private int _recentCriticalIncidents;

    // Task 5.5: AWACS picture broadcast timer
    // Requirement 5.1: AWACS broadcasts picture at 30-90 second intervals.
    private double _awacsPictureTimer;
    private double _awacsPictureInterval = 60.0; // Randomized each broadcast

    public double CommandConfidence { get; private set; } = 1.0;
    public string CommandPostureSummary { get; private set; } = "COMMAND POSTURE: STEADY.";
    public string LiveConsequenceSummary { get; private set; } = "SUPPORT CONSEQUENCE: NO LIVE COMMAND STRAIN.";

    public FriendlySupportDirector(CommManager comms, EntityManager? entityManager = null, RadarSystem? radarSystem = null)
    {
        _comms = comms;
        _entityManager = entityManager;
        _radarSystem = radarSystem;
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
            BaseReliability = relayDamaged ? 0.62 : 0.88,
            BaseRisk = 0.15,
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
            BaseReliability = relayDamaged ? 0.58 : 0.84,
            BaseRisk = 0.12,
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
            BaseReliability = 0.76,
            BaseRisk = 0.48,
            Reliability = 0.76,
            Risk = 0.48,
            BearingDeg = 285,
            RangeNm = 68,
            AltitudeFt = 26000,
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
            BaseReliability = 0.71,
            BaseRisk = 0.37,
            Reliability = 0.71,
            Risk = 0.37,
            BearingDeg = 330,
            RangeNm = 82,
            AltitudeFt = 28000,
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
            BaseReliability = 0.83,
            BaseRisk = 0.22,
            Reliability = 0.83,
            Risk = 0.22,
            BearingDeg = 018,
            RangeNm = 118,
            AltitudeFt = 32000,
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
            BaseReliability = 0.8,
            BaseRisk = 0.31,
            Reliability = 0.8,
            Risk = 0.31,
            BearingDeg = 142,
            RangeNm = 38,
            AltitudeFt = 0,
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
            BaseReliability = 0.74,
            BaseRisk = 0.18,
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
            BaseReliability = 0.68,
            BaseRisk = 0.52,
            Reliability = 0.68,
            Risk = 0.52,
            Availability = scenario == null ? SupportAvailabilityState.Unavailable : SupportAvailabilityState.Ready,
            BearingDeg = 204,
            RangeNm = 74,
            AltitudeFt = 14000,
            LastSummary = "Standing by for downed-friendly contingencies."
        });
    }

    public void Tick(double deltaTime, double gameTimeSec, SimulationSnapshot? snapshot = null)
    {
        foreach (var package in _packages)
        {
            // Task 2.1: Update CAP entity state if entity is spawned
            if (package.Type == FriendlySupportType.CombatAirPatrol && package.SpawnedEntityId != null)
            {
                UpdateCapEntityState(package);
            }

            // Task 5.3: Update AWACS entity state if entity is spawned
            if (package.Type == FriendlySupportType.Awacs && package.SpawnedEntityId != null)
            {
                UpdateAwacsEntityState(package, deltaTime, snapshot);
            }

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

                    // Task 2.1: Spawn real CAP entity when CAP becomes available
                    if (!failed && package.Type == FriendlySupportType.CombatAirPatrol && _entityManager != null)
                    {
                        SpawnCapFighter(package);
                    }

                    // Task 5.3: Spawn real AWACS entity when AWACS becomes available
                    if (!failed && package.Type == FriendlySupportType.Awacs && _entityManager != null)
                    {
                        SpawnAwacsAircraft(package);
                    }

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
            {
                package.VisibleUntilSec = Math.Max(0, package.VisibleUntilSec - deltaTime);
                UpdateVisiblePosition(package, gameTimeSec, snapshot);
            }
        }

        _pendingRequests.RemoveAll(request => gameTimeSec - request.RequestedAtSec > 900);
    }

    public void UpdateOperationalContext(
        ScenarioDefinition? scenario,
        SimulationSnapshot snapshot,
        IEnumerable<EngagementIncident>? incidents)
    {
        var recentIncidents = incidents?
            .OrderByDescending(incident => incident.TimestampUtc)
            .Take(8)
            .ToList() ?? new List<EngagementIncident>();
        _recentCriticalIncidents = recentIncidents.Count(incident => incident.Severity == IncidentSeverity.Critical);
        int recentWarnings = recentIncidents.Count(incident => incident.Severity == IncidentSeverity.Warning);

        int innerRingThreats = snapshot.HostileTracks.Count(track => track.RangeNm <= 25);
        int highThreats = snapshot.HostileTracks.Count(track => track.ThreatLevel >= 0.65);
        bool jammingPressure = snapshot.ActiveEcmEffects.Count > 0 ||
                               snapshot.HostileAircraft.Any(aircraft => aircraft.ECMActive || aircraft.Role == AircraftRole.ECMEscort);
        string priorityObjective = ResolvePriorityObjective(scenario, snapshot);

        double pressurePenalty = Math.Min(0.38, (snapshot.HostileTracks.Count * 0.03) + (innerRingThreats * 0.05) + (highThreats * 0.03));
        double incidentPenalty = Math.Min(0.42, (_recentCriticalIncidents * 0.18) + (recentWarnings * 0.06));
        CommandConfidence = Math.Clamp(1.0 - pressurePenalty - incidentPenalty, 0.25, 1.0);

        CommandPostureSummary = innerRingThreats > 0
            ? $"COMMAND POSTURE: INNER RING DEFENSE AROUND {priorityObjective}."
            : _recentCriticalIncidents > 0
                ? "COMMAND POSTURE: SAFETY-CHECKING FIRE DISCIPLINE AFTER CRITICAL INCIDENT."
                : snapshot.HostileTracks.Count >= 4
                    ? "COMMAND POSTURE: RAID RESPONSE ELEVATED WITH SUPPORT ACTORS LEANING FORWARD."
                    : "COMMAND POSTURE: CONTROLLED WATCH WITH NORMAL SUPPORT AVAILABILITY.";

        LiveConsequenceSummary = _recentCriticalIncidents > 0
            ? "SUPPORT CONSEQUENCE: COMMAND TRUST REDUCED. HIGH-RISK TASKING MAY BE DELAYED."
            : innerRingThreats > 0
                ? $"SUPPORT CONSEQUENCE: ACTIVE DEFENSIVE SHIFT TOWARD {priorityObjective}."
                : jammingPressure
                    ? "SUPPORT CONSEQUENCE: NETWORK AND PICTURE ASSETS LEANING INTO ECM PRESSURE."
                    : "SUPPORT CONSEQUENCE: SUPPORT NETWORK HOLDING NORMAL TEMPO.";

        foreach (var package in _packages)
        {
            package.Reliability = Math.Clamp(package.BaseReliability * (0.82 + (CommandConfidence * 0.18)), 0.35, 0.98);
            double dynamicRisk = package.BaseRisk + (innerRingThreats * 0.02) + (_recentCriticalIncidents * 0.04);
            if (jammingPressure && package.Type is FriendlySupportType.Awacs or FriendlySupportType.PictureRelay or FriendlySupportType.DeclarationCell)
                dynamicRisk += 0.05;
            package.Risk = Math.Clamp(dynamicRisk, 0.05, 0.95);

            if (package.Availability == SupportAvailabilityState.Tasked)
                continue;

            switch (package.Type)
            {
                case FriendlySupportType.CombatAirPatrol when snapshot.HostileTracks.Count >= 3 || innerRingThreats > 0:
                    package.VisibleUntilSec = Math.Max(package.VisibleUntilSec, 40);
                    package.LastSummary = $"Pushing intercept cover toward {priorityObjective}.";
                    break;
                case FriendlySupportType.Awacs when snapshot.HostileTracks.Count >= 4 || jammingPressure:
                    package.VisibleUntilSec = Math.Max(package.VisibleUntilSec, 55);
                    package.LastSummary = jammingPressure
                        ? "Tightening wide-area picture through ECM pressure."
                        : $"Leaning forward to maintain raid picture over {priorityObjective}.";
                    break;
                case FriendlySupportType.NearbyBattery when innerRingThreats > 0:
                    package.VisibleUntilSec = Math.Max(package.VisibleUntilSec, 35);
                    package.LastSummary = $"Crossfire lane shifted toward {priorityObjective}.";
                    break;
                case FriendlySupportType.DeclarationCell when _recentCriticalIncidents > 0:
                    package.LastSummary = "IFF desk cross-checking after weapons safety incident.";
                    break;
                case FriendlySupportType.PictureRelay when _recentCriticalIncidents > 0:
                    package.LastSummary = "Picture cell validating labels after command caution traffic.";
                    break;
                case FriendlySupportType.JammingSupport when jammingPressure:
                    package.VisibleUntilSec = Math.Max(package.VisibleUntilSec, 30);
                    package.LastSummary = "Support jammer posture tightened under electronic pressure.";
                    break;
            }
        }
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

        if (_recentCriticalIncidents > 0 &&
            CommandConfidence < 0.45 &&
            type is FriendlySupportType.CombatAirPatrol or FriendlySupportType.NearbyBattery)
        {
            return new SupportRequestResult(
                false,
                $"{package.DisplayName} holding pending command fire-discipline review before further high-risk tasking.",
                package.Id,
                0,
                false);
        }

        double reliabilityDelay = package.Reliability < 0.6
            ? (0.6 - package.Reliability) * 0.5
            : 0.0;
        double eta = Math.Ceiling(GetDelay(type) * (1.0 + reliabilityDelay + ((1.0 - CommandConfidence) * 0.45)));
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
            : $"FRIENDLY SUPPORT: CONF {CommandConfidence:P0} // " + string.Join(" || ", _packages.Select(package => package.StatusLine));

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

    private static void UpdateVisiblePosition(FriendlySupportPackage package, double gameTimeSec, SimulationSnapshot? snapshot = null)
    {
        double orbitPhase = gameTimeSec / 18.0;
        switch (package.Type)
        {
            case FriendlySupportType.CombatAirPatrol:
                // If there are hostile tracks, vector toward the highest-threat one
                // rather than just orbiting. CAP moves at ~450kts (~0.25nm/s).
                var primaryThreat = snapshot?.HostileTracks
                    .Where(t => t.IsHot && t.RangeNm < 120)
                    .OrderByDescending(t => t.ThreatLevel)
                    .FirstOrDefault();

                if (primaryThreat != null)
                {
                    // Steer CAP bearing toward threat bearing, close range by ~0.25nm/s
                    double bearingDiff = NormalizeBearing(primaryThreat.BearingDeg - package.BearingDeg);
                    if (bearingDiff > 180) bearingDiff -= 360;
                    package.BearingDeg = NormalizeBearing(package.BearingDeg + Math.Sign(bearingDiff) * Math.Min(Math.Abs(bearingDiff), 1.5));

                    double rangeDiff = primaryThreat.RangeNm - package.RangeNm;
                    // Close to intercept range (stay ~15nm outside threat, don't fly into SAM envelope)
                    double targetRange = Math.Max(primaryThreat.RangeNm + 15, 30);
                    double rangeStep = Math.Sign(package.RangeNm - targetRange) * Math.Min(Math.Abs(package.RangeNm - targetRange), 0.25);
                    package.RangeNm = Math.Clamp(package.RangeNm - rangeStep, 20, 120);
                    package.AltitudeFt = 25000 + Math.Sin(orbitPhase * 0.8) * 1800;
                }
                else
                {
                    // No active threat — hold CAP orbit west of sector
                    package.BearingDeg = NormalizeBearing(285 + Math.Sin(orbitPhase) * 24);
                    package.RangeNm = 64 + Math.Cos(orbitPhase) * 6;
                    package.AltitudeFt = 25000 + Math.Sin(orbitPhase * 0.8) * 1800;
                }
                break;
            case FriendlySupportType.JammingSupport:
                package.BearingDeg = NormalizeBearing(330 + Math.Sin(orbitPhase * 0.7) * 10);
                package.RangeNm = 78 + Math.Cos(orbitPhase * 0.7) * 4;
                package.AltitudeFt = 28500 + Math.Sin(orbitPhase * 0.5) * 1200;
                break;
            case FriendlySupportType.Awacs:
                package.BearingDeg = NormalizeBearing(18 + Math.Sin(orbitPhase * 0.35) * 8);
                package.RangeNm = 116 + Math.Cos(orbitPhase * 0.35) * 3;
                package.AltitudeFt = 32000 + Math.Sin(orbitPhase * 0.25) * 900;
                break;
            case FriendlySupportType.NearbyBattery:
                package.BearingDeg = 142;
                package.RangeNm = 38;
                package.AltitudeFt = 0;
                break;
            case FriendlySupportType.SearchAndRescue:
                package.BearingDeg = NormalizeBearing(204 + Math.Sin(orbitPhase * 0.9) * 14);
                package.RangeNm = 70 + Math.Cos(orbitPhase * 0.9) * 5;
                package.AltitudeFt = 14500 + Math.Sin(orbitPhase * 0.7) * 800;
                break;
        }
    }

    private static double NormalizeBearing(double value) => (value % 360 + 360) % 360;

    private static string ResolvePriorityObjective(ScenarioDefinition? scenario, SimulationSnapshot snapshot)
    {
        if (scenario == null || scenario.SectorMap.Objectives.Count == 0)
            return "INNER RING";

        var pressured = scenario.SectorMap.Objectives.FirstOrDefault(objective =>
            snapshot.HostileTracks.Any(track =>
                Math.Abs(NormalizeAngle(track.BearingDeg - objective.BearingDeg)) < 18 &&
                track.RangeNm <= objective.RangeNm + 12));

        return (pressured ?? scenario.SectorMap.Objectives.First()).Name.ToUpperInvariant();
    }

    private static double NormalizeAngle(double degrees)
    {
        double normalized = degrees % 360;
        if (normalized > 180) normalized -= 360;
        if (normalized < -180) normalized += 360;
        return normalized;
    }

    /// <summary>
    /// Task 5.3: Spawns a real AWACS entity when AWACS support becomes available.
    /// Wires CommManager and configures racetrack orbit parameters.
    /// Requirement 4.1: AWACS entity at 30,000 feet altitude with orbit pattern.
    /// Requirement 4.7: AWACS availability tracked via entity state.
    /// </summary>
    private void SpawnAwacsAircraft(FriendlySupportPackage package)
    {
        if (_entityManager == null)
            return;

        // Calculate orbit center from package position
        Vec2 orbitCenter = CoordinateSystem.FromBearingRange(package.BearingDeg, package.RangeNm);

        var awacs = new AWACSAircraft
        {
            // Identity
            CallSign = package.UnitCallsign,
            Designation = package.Designation,
            Affiliation = Affiliation.Friendly,
            // Position: start at orbit center, heading east
            Position = orbitCenter,
            HeadingDeg = 090,
            AltitudeM = CoordinateSystem.FtToM(30000),
            SpeedMps = CoordinateSystem.KtsToMps(460), // Cruise
            // Racetrack orbit parameters
            RacetrackLegLengthNm = 60.0,
            RacetrackHeadingDeg = 090,
            RacetrackTurnRadiusNm = 10.0,
            RadarCoverageRadiusNm = 200.0,
            // Behavior
            CurrentBehavior = AircraftBehavior.OrbitPatrol,
            SpawnTime = DateTime.UtcNow
        };

        // Wire CommManager for radio communications (Task 5.5)
        awacs.CommManager = _comms;

        // Sync physics state
        awacs.SyncPhysicsState();

        // Register with EntityManager
        _entityManager.Add(awacs);

        // Track entity ID in package
        package.SpawnedEntityId = awacs.Id;

        // Send on-station radio message
        _comms.Queue(CommManager.CreateMessage(
            RadioRules.CreateFriendlySupportProfile(package.DisplayName, package.RankOrRole, package.UnitCallsign, package.Designation),
            RadioChannel.IntelNet,
            $"{package.UnitCallsign}, on station, angels 30, establishing wide-area picture. Ready to support.",
            MessagePriority.Priority,
            MessageType.StatusReport,
            recipient: "ALPHA",
            canReply: true,
            staticLevel: 0.10));
    }

    /// <summary>
    /// Task 5.3: Updates AWACS availability based on spawned entity state.
    /// Mirrors UpdateCapEntityState for the AWACS lifecycle.
    /// Requirement 4.7: AWACS availability tracked via entity state.
    /// </summary>
    private void UpdateAwacsEntityState(FriendlySupportPackage package, double deltaTime, SimulationSnapshot? snapshot)
    {
        if (_entityManager == null || package.SpawnedEntityId == null)
            return;

        var entity = _entityManager.Get(package.SpawnedEntityId);

        // Entity destroyed — update availability to damaged
        if (entity == null || entity.Status == EntityStatus.Destroyed)
        {
            if (entity != null && entity.Status == EntityStatus.Destroyed)
                _entityManager.Remove(package.SpawnedEntityId, "AWACS destroyed");

            package.SpawnedEntityId = null;
            package.Availability = SupportAvailabilityState.Damaged;
            package.CooldownRemainingSec = GetCooldown(package.Type);
            package.LastSummary = $"{package.UnitCallsign}, picture link lost. Rebuilding coverage.";

            _comms.Queue(CommManager.CreateMessage(
                RadioRules.CreateFriendlySupportProfile(package.DisplayName, package.RankOrRole, package.UnitCallsign, package.Designation),
                RadioChannel.IntelNet,
                package.LastSummary,
                MessagePriority.Immediate,
                MessageType.Alert,
                recipient: "ALPHA",
                canReply: false,
                staticLevel: 0.3));
            return;
        }

        // AWACS bingo fuel — RTB
        if (entity is Aircraft awacsAircraft && awacsAircraft.IsOnRTB)
        {
            _entityManager.Remove(package.SpawnedEntityId, "AWACS bingo fuel, RTB");
            package.SpawnedEntityId = null;
            package.Availability = SupportAvailabilityState.CoolingDown;
            package.CooldownRemainingSec = GetCooldown(package.Type);
            package.LastSummary = $"{package.UnitCallsign}, bingo fuel, departing station, picture handoff to GCI.";

            _comms.Queue(CommManager.CreateMessage(
                RadioRules.CreateFriendlySupportProfile(package.DisplayName, package.RankOrRole, package.UnitCallsign, package.Designation),
                RadioChannel.IntelNet,
                package.LastSummary,
                MessagePriority.Priority,
                MessageType.StatusReport,
                recipient: "ALPHA",
                canReply: false,
                staticLevel: 0.12));
            return;
        }

        // Task 5.5: AWACS periodic picture broadcast.
        // Requirement 5.1: Picture at 30-90 second intervals.
        // Requirement 5.2: Use Bullseye reference format.
        _awacsPictureTimer += deltaTime; // Increment by actual simulation delta
        if (_awacsPictureTimer >= _awacsPictureInterval)
        {
            _awacsPictureTimer = 0;
            // Randomize next interval 30-90 seconds
            _awacsPictureInterval = 30.0 + SimulationRandom.Instance.NextDouble() * 60.0;
            SendAwacsPicture(package, snapshot);
        }
    }

    /// <summary>
    /// Task 5.5: Generates and queues an AWACS air picture broadcast in Bullseye format.
    /// Requirement 5.2: Use Bullseye reference (battery = Bulls) for group positions.
    /// Requirement 5.3: Include track count, group bearings, altitudes.
    /// Requirement 5.4: Broadcast defensive status when under heavy attack.
    /// Requirement 5.5: Use IntelNet channel for picture traffic.
    /// </summary>
    private void SendAwacsPicture(FriendlySupportPackage package, SimulationSnapshot? snapshot)
    {
        if (snapshot == null) return;

        var speaker = RadioRules.CreateFriendlySupportProfile(
            package.DisplayName, package.RankOrRole, package.UnitCallsign, package.Designation);

        // Build group picture from hostile tracks
        var hostileTracks = snapshot.HostileTracks;
        int trackCount = hostileTracks.Count;

        string pictureContent;
        if (trackCount == 0)
        {
            // Requirement 5.4: Defensive status — all clear
            pictureContent = $"{package.UnitCallsign}, PICTURE CLEAN. No hostile tracks at this time.";
        }
        else if (trackCount <= 3)
        {
            // Small raid — call individual groups
            var groups = hostileTracks
                .OrderByDescending(t => t.ThreatLevel)
                .Take(3)
                .Select(t =>
                    $"SINGLE GROUP BEARING {t.BearingDeg:000}, {t.RangeNm:F0}NM, ANGELS {(int)(t.AltitudeFt / 1000)}, {t.AspectString}")
                .ToList();

            pictureContent = $"{package.UnitCallsign}, PICTURE: {string.Join(". ", groups)}.";
        }
        else
        {
            // Large raid — summarize by sector and call primary threat
            var primaryThreat = hostileTracks.OrderByDescending(t => t.ThreatLevel).First();
            int inbound = hostileTracks.Count(t => t.IsHot);
            pictureContent = $"{package.UnitCallsign}, PICTURE: {trackCount} GROUPS. PRIMARY THREAT BEARING " +
                $"{primaryThreat.BearingDeg:000}, {primaryThreat.RangeNm:F0}NM, ANGELS {(int)(primaryThreat.AltitudeFt / 1000)}. " +
                $"{inbound} GROUPS INBOUND HOT.";
        }

        // Requirement 5.4: Append DEFENSIVE call when under heavy pressure
        bool defensiveCall = trackCount >= 6 || hostileTracks.Any(t => t.RangeNm < 20 && t.ThreatLevel > 0.7);
        if (defensiveCall)
            pictureContent += $" {package.UnitCallsign}, DEFENSIVE.";

        _comms.Queue(CommManager.CreateMessage(
            speaker,
            RadioChannel.IntelNet,
            pictureContent,
            MessagePriority.Routine,
            MessageType.IntelUpdate,
            recipient: "ALPHA",
            canReply: false,
            staticLevel: 0.08));
    }

    /// <summary>
    /// Task 2.1: Spawns a real CAP fighter entity when CAP support becomes available.
    /// Task 4.4: Wires CommManager to CAP fighter for radio communications.
    /// </summary>
    private void SpawnCapFighter(FriendlySupportPackage package)
    {
        if (_entityManager == null)
            return;

        // Calculate patrol sector from package position
        Vec2 sectorCenter = CoordinateSystem.FromBearingRange(package.BearingDeg, package.RangeNm);
        double sectorRadiusNm = 15.0; // Standard CAP patrol sector radius

        // Spawn the fighter with default loadout (4x AIM-120, 2x AIM-9)
        var fighter = _entityManager.SpawnFriendlyFighter(
            callsign: package.UnitCallsign,
            sectorId: package.Id,
            sectorCenter: sectorCenter,
            sectorRadiusNm: sectorRadiusNm,
            aim120Count: 4,
            aim9Count: 2);

        // Task 4.4: Wire CommManager to enable radio communications
        fighter.CommManager = _comms;

        // Track the spawned entity ID
        package.SpawnedEntityId = fighter.Id;
    }

    /// <summary>
    /// Task 2.1 & 2.2: Updates CAP availability based on spawned entity state.
    /// Called during Tick to check if spawned CAP fighters are still active.
    /// Task 2.2: Despawns CAP fighters when they RTB or are destroyed.
    /// Task 4.2: Updates intercept course when CAP fighter is intercepting a target.
    /// </summary>
    private void UpdateCapEntityState(FriendlySupportPackage package)
    {
        if (_entityManager == null || package.SpawnedEntityId == null)
            return;

        var entity = _entityManager.Get(package.SpawnedEntityId);
        
        // If entity no longer exists or is destroyed, update availability
        if (entity == null || entity.Status == EntityStatus.Destroyed)
        {
            // Task 2.2: Despawn the entity if it still exists (destroyed but not yet removed)
            if (entity != null && entity.Status == EntityStatus.Destroyed)
            {
                _entityManager.Remove(package.SpawnedEntityId, "CAP fighter destroyed");
            }
            
            package.SpawnedEntityId = null;
            package.Availability = SupportAvailabilityState.Damaged;
            package.CooldownRemainingSec = GetCooldown(package.Type);
            package.LastSummary = "CAP fighter lost. Rebuilding coverage.";
            return;
        }
        
        // Task 2.2: If entity is RTB (Winchester or bingo fuel), despawn and start cooldown
        if (entity is Aircraft aircraft && (aircraft.IsOnRTB || aircraft.IsWinchester))
        {
            // Despawn the CAP fighter when it RTBs
            _entityManager.Remove(package.SpawnedEntityId, aircraft.IsWinchester 
                ? "CAP fighter Winchester, RTB" 
                : "CAP fighter bingo fuel, RTB");
            
            package.SpawnedEntityId = null;
            package.Availability = SupportAvailabilityState.CoolingDown;
            package.CooldownRemainingSec = GetCooldown(package.Type);
            package.LastSummary = aircraft.IsWinchester 
                ? "CAP fighter Winchester, returning to base."
                : "CAP fighter bingo fuel, returning to base.";
            return;
        }

        // Task 4.2: Update intercept course if CAP fighter is intercepting a target
        if (entity is Aircraft capFighter && !string.IsNullOrEmpty(capFighter.InterceptTargetTrackId))
        {
            UpdateInterceptCourse(capFighter);
        }
    }

    /// <summary>
    /// Task 4.2: Updates the intercept course for a CAP fighter based on current target position and velocity.
    /// Requirement 3.4: WHILE intercepting, THE CAP_Fighter SHALL update its course if the target Track maneuvers.
    /// Requirement 3.5: IF the target Track is destroyed before intercept, THEN THE CAP_Fighter SHALL report target lost and resume patrol.
    /// </summary>
    private void UpdateInterceptCourse(Aircraft capFighter)
    {
        if (_radarSystem == null || string.IsNullOrEmpty(capFighter.InterceptTargetTrackId))
            return;

        // Get the target track from RadarSystem
        var targetTrack = _radarSystem.TrackManager.GetById(capFighter.InterceptTargetTrackId);
        
        // If target track is lost or destroyed, resume patrol
        if (targetTrack == null || targetTrack.Quality == TrackQuality.Lost)
        {
            capFighter.ResumePatrol();
            
            // Send target lost report
            if (capFighter.CommManager != null && !string.IsNullOrEmpty(capFighter.CallSign))
            {
                var speaker = RadioRules.CreateFriendlySupportProfile(
                    capFighter.CallSign,
                    "CAP FIGHTER",
                    capFighter.CallSign,
                    capFighter.Designation);

                var message = CommManager.CreateMessage(
                    speaker,
                    RadioChannel.CommandNet,
                    $"{capFighter.CallSign}, target lost, resuming patrol",
                    MessagePriority.Routine,
                    MessageType.StatusReport,
                    recipient: "ALPHA ACTUAL",
                    canReply: false,
                    staticLevel: 0.15);

                _comms.Queue(message);
            }
            return;
        }

        // Calculate target position and velocity from track
        Vec2 targetPosition = targetTrack.Position;
        
        // Estimate target velocity from track heading and speed
        // Convert heading to radians and calculate velocity vector
        double headingRad = targetTrack.HeadingDeg * Math.PI / 180.0;
        double speedMps = targetTrack.SpeedKts * 0.514444; // Convert knots to m/s
        Vec2 targetVelocity = new Vec2(
            Math.Sin(headingRad) * speedMps,
            Math.Cos(headingRad) * speedMps
        );

        // Update intercept course
        capFighter.SetInterceptCourse(targetPosition, targetVelocity);
    }
}
