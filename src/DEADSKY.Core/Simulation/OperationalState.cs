using DEADSKY.Core.Campaign;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Weapons;

namespace DEADSKY.Core.Simulation;

public enum LockCueState
{
    Search,
    TrackHold,
    HardLock,
    MissileInbound
}

public enum IncidentSeverity
{
    Info,
    Warning,
    Critical
}

public enum TacticalMarkerKind
{
    Battery,
    HostileTrack,
    AssumedHostileTrack,
    FriendlyTrack,
    CivilianTrack,
    UnknownTrack,
    ActiveMissile,
    FriendlySupport,
    ObjectivePrimary,
    ObjectiveSecondary,
    ObjectiveThreatened,
    ObjectiveBreached,
    Landmark,
    Jamming
}

public enum MarkerVisibilityMode
{
    Always,
    LabelsWhenEnabled,
    FocusOnly
}

public sealed record TrackThreatState(
    string TrackId,
    LockCueState LockCue,
    bool RadarSupportAvailable,
    bool MissileInbound,
    bool CountermeasureActive,
    double ClassificationConfidence,
    bool EngagementValid,
    string? RecommendedWeaponId,
    string FriendlyFireRisk,
    string CurrentCountermeasureType,
    string SupportRequirementStatus,
    double Confidence,
    string Summary);

public sealed record FriendlyForceState(
    string Id,
    string Callsign,
    string Role,
    string MarkerClass,
    string Status,
    bool VisibleInPicture,
    string MissionTask,
    string VisibilityReason,
    double AvailabilityWindowSec,
    double HeadingDeg,
    double SpeedKts,
    string Summary,
    Vec2 Position,
    double AltitudeFt);

public sealed record ObjectiveTacticalState(
    string ObjectiveId,
    string Name,
    string Importance,
    string Status,
    string Summary,
    double BearingDeg,
    double RangeNm,
    Vec2 Position,
    bool IsThreatened,
    bool IsUnderAttack,
    bool IsBreached);

public sealed record TacticalMarkerState(
    string MarkerId,
    TacticalMarkerKind Kind,
    string SourceSystem,
    string Label,
    string IconKey,
    string Details,
    double BearingDeg,
    double RangeNm,
    Vec2 Position,
    int Priority,
    MarkerVisibilityMode VisibilityMode,
    bool IsSelectedRelated);

public sealed record SelectedTrackContext(
    string TrackId,
    string Summary,
    string DoctrineSummary,
    string WeaponGating,
    string SupportRelationship,
    string CommandCaveats,
    string RecommendedAction);

public sealed record CommsConsequenceState(
    string RoeSummary,
    string CommandTrustSummary,
    string SupportImpactSummary,
    string OutstandingWarning);

public sealed record EngagementIncident(
    string IncidentType,
    string Summary,
    IncidentSeverity Severity,
    DateTime TimestampUtc,
    string? TrackId = null,
    string? EntityId = null,
    string? WeaponId = null);

public sealed record SharedOperationalPicture(
    string ScenarioHeader,
    string ScenarioNotes,
    string ThreatSummary,
    string SupportSummary,
    string ConsequenceSummary,
    string RecommendedActionSummary,
    IReadOnlyList<TrackThreatState> ThreatStates,
    IReadOnlyList<FriendlyForceState> FriendlyForces,
    IReadOnlyList<ObjectiveTacticalState> ObjectiveStates,
    IReadOnlyList<TacticalMarkerState> TacticalMarkers,
    SelectedTrackContext? SelectedTrack,
    CommsConsequenceState CommsConsequences);

public static class OperationalPictureBuilder
{
    public static IReadOnlyList<TrackThreatState> BuildThreatStates(SimulationSnapshot snapshot)
    {
        var states = new List<TrackThreatState>();
        foreach (var track in snapshot.AllTracks)
        {
            bool hardLock = track.IsDesignated ||
                            (!string.IsNullOrWhiteSpace(snapshot.Battery?.DesignatedTargetId) &&
                             snapshot.Battery.DesignatedTargetId == track.EntityId);
            bool trackHold = track.IsTrackHeld && !hardLock;
            bool missileInbound = track.IsBeingEngaged;
            bool countermeasureActive = false;
            string countermeasureType = "None";

            var aircraft = track.EntityId == null
                ? null
                : snapshot.HostileAircraft.FirstOrDefault(entity => entity.Id == track.EntityId);
            if (aircraft != null)
            {
                countermeasureActive = aircraft.ECMActive || aircraft.IsChaffActive || aircraft.IsFlareActive;
                countermeasureType = ResolveCountermeasureType(aircraft);
            }

            LockCueState cue = missileInbound
                ? LockCueState.MissileInbound
                : hardLock
                    ? LockCueState.HardLock
                    : trackHold
                        ? LockCueState.TrackHold
                        : LockCueState.Search;

            bool radarSupport = snapshot.Battery?.RadarOnline == true &&
                                snapshot.Battery.RadarMode is RadarMode.TrackWhileScan or RadarMode.SingleTargetTrack;

            string summary = cue switch
            {
                LockCueState.MissileInbound => "Missile support active.",
                LockCueState.HardLock => "Hard-lock warning.",
                LockCueState.TrackHold => "Tracked and sorted.",
                _ => "Search picture only."
            };

            if (countermeasureActive)
                summary += " Countermeasures detected.";

            var recommendedWeapon = ResolveRecommendedWeapon(snapshot, track, out bool engagementValid, out string supportRequirement, out _);
            string friendlyFireRisk = BuildFriendlyFireRisk(track, snapshot.Battery);

            states.Add(new TrackThreatState(
                track.TrackId,
                cue,
                radarSupport,
                missileInbound,
                countermeasureActive,
                Math.Clamp(track.ClassificationConfidence, 0.0, 1.0),
                engagementValid,
                recommendedWeapon?.Id,
                friendlyFireRisk,
                countermeasureType,
                supportRequirement,
                Math.Clamp(track.ClassificationConfidence, 0.0, 1.0),
                summary));
        }

        return states;
    }

    public static IReadOnlyList<FriendlyForceState> BuildFriendlyForceStates(IEnumerable<FriendlySupportPackage>? supportPackages)
    {
        if (supportPackages == null)
            return Array.Empty<FriendlyForceState>();

        return supportPackages
            .Select(package => new FriendlyForceState(
                package.Id,
                package.UnitCallsign,
                package.Type.ToString(),
                BuildSupportMarkerClass(package.Type),
                package.Availability.ToString(),
                package.IsVisibleInPicture,
                BuildSupportMissionTask(package),
                package.IsVisibleInPicture ? "Visible due to active tasking or sector pressure." : "Off picture until tasked or pushed forward.",
                package.VisibleUntilSec,
                package.BearingDeg,
                EstimateSupportSpeed(package.Type),
                package.LastSummary,
                package.Position,
                package.AltitudeFt))
            .ToList();
    }

    public static IReadOnlyList<ObjectiveTacticalState> BuildObjectiveStates(
        ScenarioDefinition? scenario,
        SimulationSnapshot snapshot,
        IEnumerable<EngagementIncident>? incidents = null)
    {
        if (scenario?.SectorMap.Objectives.Count is not > 0)
            return Array.Empty<ObjectiveTacticalState>();

        var recentIncidents = incidents?.TakeLast(12).ToList() ?? new List<EngagementIncident>();
        return scenario.SectorMap.Objectives
            .Select(objective =>
            {
                bool targeted = scenario.EnemyForces.Waves.Any(w => w.TargetObjectiveId.Equals(objective.Id, StringComparison.OrdinalIgnoreCase));
                int closeThreats = snapshot.HostileTracks.Count(track =>
                    Math.Abs(NormalizeAngle(track.BearingDeg - objective.BearingDeg)) < 14 &&
                    track.RangeNm <= objective.RangeNm + 8);
                bool shadowed = snapshot.HostileTracks.Any(track =>
                    Math.Abs(NormalizeAngle(track.BearingDeg - objective.BearingDeg)) < 22 &&
                    track.RangeNm <= objective.RangeNm + 20);
                bool breached = snapshot.HostileTracks.Any(track =>
                    Math.Abs(NormalizeAngle(track.BearingDeg - objective.BearingDeg)) < 10 &&
                    track.RangeNm <= Math.Max(8, objective.RangeNm - 2));
                bool incidentMention = recentIncidents.Any(incident =>
                    incident.Summary.Contains(objective.Name, StringComparison.OrdinalIgnoreCase));

                string status = breached
                    ? "BREACHED"
                    : closeThreats > 0 || incidentMention
                        ? "UNDER ATTACK"
                        : shadowed
                            ? "THREATENED"
                            : targeted
                                ? "TARGETED"
                                : "WATCH";

                string summary = status switch
                {
                    "BREACHED" => $"{objective.Name} is inside the hostile penetration envelope.",
                    "UNDER ATTACK" => $"{objective.Name} is under direct pressure from the active raid.",
                    "THREATENED" => $"{objective.Name} is shadowed by nearby hostile approaches.",
                    "TARGETED" => $"{objective.Name} is a planned objective for one or more hostile packages.",
                    _ => $"{objective.Name} remains under sector watch."
                };

                return new ObjectiveTacticalState(
                    objective.Id,
                    objective.Name,
                    objective.Importance,
                    status,
                    summary,
                    objective.BearingDeg,
                    objective.RangeNm,
                    CoordinateSystem.FromBearingRange(objective.BearingDeg, objective.RangeNm),
                    shadowed || targeted || closeThreats > 0,
                    closeThreats > 0 || incidentMention,
                    breached);
            })
            .ToList();
    }

    public static IReadOnlyList<TacticalMarkerState> BuildTacticalMarkers(
        SimulationSnapshot snapshot,
        ScenarioDefinition? scenario,
        IReadOnlyList<ObjectiveTacticalState> objectives,
        IReadOnlyList<FriendlyForceState> friendlyForces)
    {
        var markers = new List<TacticalMarkerState>
        {
            new(
                "battery-alpha",
                TacticalMarkerKind.Battery,
                "simulation",
                snapshot.Battery?.Callsign ?? "ALPHA",
                "battery",
                "Primary firing unit.",
                0,
                0,
                Vec2.Zero,
                100,
                MarkerVisibilityMode.Always,
                false)
        };

        foreach (var objective in objectives)
        {
            TacticalMarkerKind kind = objective.Status switch
            {
                "BREACHED" => TacticalMarkerKind.ObjectiveBreached,
                "UNDER ATTACK" or "THREATENED" => TacticalMarkerKind.ObjectiveThreatened,
                _ when objective.Importance.Equals("primary", StringComparison.OrdinalIgnoreCase) => TacticalMarkerKind.ObjectivePrimary,
                _ => TacticalMarkerKind.ObjectiveSecondary
            };

            markers.Add(new TacticalMarkerState(
                $"objective-{objective.ObjectiveId}",
                kind,
                "scenario",
                objective.Name,
                objective.Importance.Equals("primary", StringComparison.OrdinalIgnoreCase) ? "objective-primary" : "objective-secondary",
                objective.Summary,
                objective.BearingDeg,
                objective.RangeNm,
                objective.Position,
                objective.IsBreached ? 95 : objective.IsUnderAttack ? 88 : 74,
                MarkerVisibilityMode.Always,
                objective.IsThreatened));
        }

        if (scenario != null)
        {
            foreach (var landmark in scenario.SectorMap.Landmarks)
            {
                markers.Add(new TacticalMarkerState(
                    $"landmark-{landmark.Name.ToLowerInvariant().Replace(' ', '-')}",
                    TacticalMarkerKind.Landmark,
                    "scenario",
                    landmark.Name,
                    $"landmark-{landmark.Category.ToLowerInvariant()}",
                    $"Terrain feature: {landmark.Category}.",
                    landmark.BearingDeg,
                    landmark.RangeNm,
                    CoordinateSystem.FromBearingRange(landmark.BearingDeg, landmark.RangeNm),
                    30,
                    MarkerVisibilityMode.LabelsWhenEnabled,
                    false));
            }
        }

        foreach (var track in snapshot.AllTracks)
        {
            markers.Add(new TacticalMarkerState(
                track.TrackId,
                track.Classification switch
                {
                    TrackClassification.Hostile => TacticalMarkerKind.HostileTrack,
                    TrackClassification.AssumedHostile => TacticalMarkerKind.AssumedHostileTrack,
                    TrackClassification.Friendly => TacticalMarkerKind.FriendlyTrack,
                    TrackClassification.Civilian or TrackClassification.Neutral => TacticalMarkerKind.CivilianTrack,
                    _ => TacticalMarkerKind.UnknownTrack
                },
                "radar",
                $"{track.TrackDesignation} {track.TrackId}",
                "track",
                $"Track {track.TrackId} {track.Classification.ToString().ToUpperInvariant()} {track.RangeNm:0.0}NM.",
                track.BearingDeg,
                track.RangeNm,
                track.Position,
                track.IsBeingEngaged ? 92 : track.IsDesignated ? 90 : track.IsTrackHeld ? 76 : 55,
                MarkerVisibilityMode.FocusOnly,
                track.IsDesignated || track.IsTrackHeld || track.IsBeingEngaged));
        }

        foreach (var missile in snapshot.ActiveMissiles)
        {
            markers.Add(new TacticalMarkerState(
                missile.Id,
                TacticalMarkerKind.ActiveMissile,
                "weapons",
                missile.MissileTypeName,
                "missile",
                $"Active missile {missile.MissileTypeName}.",
                CoordinateSystem.ToBearingRange(missile.Position).bearingDeg,
                CoordinateSystem.MetersToNm(missile.Position.Length),
                missile.Position,
                86,
                MarkerVisibilityMode.FocusOnly,
                true));
        }

        foreach (var jamming in snapshot.ActiveEcmEffects)
        {
            markers.Add(new TacticalMarkerState(
                $"jam-{jamming.BearingDeg:000}-{jamming.StrengthNormalized:0.00}",
                TacticalMarkerKind.Jamming,
                "radar",
                "JAMMING",
                "ecm",
                $"Electronic interference strength {jamming.StrengthNormalized:P0}.",
                jamming.BearingDeg,
                snapshot.RadarRangeNm * 0.72,
                CoordinateSystem.FromBearingRange(jamming.BearingDeg, snapshot.RadarRangeNm * 0.72),
                66,
                MarkerVisibilityMode.LabelsWhenEnabled,
                false));
        }

        foreach (var force in friendlyForces.Where(force => force.VisibleInPicture))
        {
            markers.Add(new TacticalMarkerState(
                force.Id,
                TacticalMarkerKind.FriendlySupport,
                "support",
                force.Callsign,
                force.MarkerClass,
                force.Summary,
                CoordinateSystem.ToBearingRange(force.Position).bearingDeg,
                CoordinateSystem.MetersToNm(force.Position.Length),
                force.Position,
                70,
                MarkerVisibilityMode.LabelsWhenEnabled,
                false));
        }

        return markers
            .OrderByDescending(marker => marker.Priority)
            .ToList();
    }

    public static SelectedTrackContext? BuildSelectedTrackContext(
        SimulationSnapshot snapshot,
        ScenarioDefinition? scenario,
        string? selectedTrackId,
        IReadOnlyList<TrackThreatState> threatStates,
        IReadOnlyList<ObjectiveTacticalState> objectives)
    {
        if (string.IsNullOrWhiteSpace(selectedTrackId))
            return null;

        var track = snapshot.AllTracks.FirstOrDefault(candidate => candidate.TrackId.Equals(selectedTrackId, StringComparison.OrdinalIgnoreCase));
        if (track == null)
            return null;

        var threatState = threatStates.FirstOrDefault(state => state.TrackId.Equals(track.TrackId, StringComparison.OrdinalIgnoreCase));
        var targetObjective = ResolveClosestObjective(track, objectives);
        var recommendedWeapon = threatState?.RecommendedWeaponId == null
            ? null
            : snapshot.AvailableWeapons.FirstOrDefault(weapon => weapon.Id.Equals(threatState.RecommendedWeaponId, StringComparison.OrdinalIgnoreCase));

        string doctrineSummary = targetObjective == null
            ? "No direct objective pressure link established."
            : $"{track.TrackDesignation} is the nearest active threat to {targetObjective.Name}.";
        string weaponGating = threatState == null
            ? "No live fire-control assessment."
            : threatState.EngagementValid
                ? $"Recommended {recommendedWeapon?.ShortCode ?? threatState.RecommendedWeaponId ?? "weapon"} is currently valid."
                : $"Current shot blocked. {threatState.SupportRequirementStatus}";
        string supportRelationship = targetObjective == null
            ? "Support relevance: maintain sector picture and classify the track."
            : $"Support relevance: picture, declare, and CAP all reinforce {targetObjective.Name}.";
        string commandCaveats = BuildCommandCaveat(track, snapshot.Battery);
        string recommendedAction = threatState?.EngagementValid == true
            ? $"Sort, designate, and prosecute {track.TrackId} before it pressures the inner ring."
            : $"Hold or sort {track.TrackId} until {NormalizeSentenceFragment(threatState?.SupportRequirementStatus ?? "the shot opens")}.";

        return new SelectedTrackContext(
            track.TrackId,
            $"{track.TrackDesignation} {track.TrackId} {track.RangeNm:0.0}NM {track.AspectString}.",
            doctrineSummary,
            weaponGating,
            supportRelationship,
            commandCaveats,
            recommendedAction);
    }

    public static CommsConsequenceState BuildCommsConsequences(
        IEnumerable<EngagementIncident>? incidents,
        double commandConfidence,
        string commandPostureSummary,
        string supportConsequenceSummary)
    {
        var recent = incidents?
            .OrderByDescending(incident => incident.TimestampUtc)
            .Take(6)
            .ToList() ?? new List<EngagementIncident>();
        var latestWarning = recent.FirstOrDefault(incident => incident.Severity >= IncidentSeverity.Warning);
        string roeSummary = latestWarning == null
            ? "ROE stable. No immediate command override traffic."
            : $"{latestWarning.IncidentType.Replace('_', ' ')} driving command caution.";
        string trustSummary = $"Command confidence {commandConfidence:P0}. {commandPostureSummary}";
        string outstanding = latestWarning == null
            ? "No unresolved warnings."
            : latestWarning.Summary;

        return new CommsConsequenceState(
            roeSummary,
            trustSummary,
            supportConsequenceSummary,
            outstanding);
    }

    public static SharedOperationalPicture Build(
        SimulationSnapshot snapshot,
        ScenarioDefinition? scenario = null,
        IEnumerable<EngagementIncident>? incidents = null,
        IEnumerable<FriendlySupportPackage>? supportPackages = null,
        string? selectedTrackId = null,
        double commandConfidence = 1.0,
        string commandPostureSummary = "COMMAND POSTURE: STEADY.",
        string supportConsequenceSummary = "SUPPORT CONSEQUENCE: NO LIVE COMMAND STRAIN.")
    {
        var threatStates = BuildThreatStates(snapshot);
        var friendlyForces = BuildFriendlyForceStates(supportPackages);
        var objectiveStates = BuildObjectiveStates(scenario, snapshot, incidents);
        var tacticalMarkers = BuildTacticalMarkers(snapshot, scenario, objectiveStates, friendlyForces);
        var selectedTrack = BuildSelectedTrackContext(snapshot, scenario, selectedTrackId, threatStates, objectiveStates);
        var commsConsequences = BuildCommsConsequences(
            incidents,
            commandConfidence,
            commandPostureSummary,
            supportConsequenceSummary);
        var recentIncident = incidents?
            .OrderByDescending(incident => incident.TimestampUtc)
            .FirstOrDefault();

        string scenarioHeader = scenario == null
            ? "TACTICAL MAP // NO ACTIVE THEATER"
            : $"TACTICAL MAP // {scenario.SectorMap.TheaterName.ToUpperInvariant()}";
        string scenarioNotes = BuildScenarioNotes(scenario, objectiveStates);
        string threatSummary = snapshot.HostileTracks.Count == 0
            ? "Air picture clean."
            : $"{snapshot.HostileTracks.Count} hostile tracks, {snapshot.ActiveMissiles.Count} friendly missiles in flight.";
        string supportSummary = friendlyForces.Count == 0
            ? "No visible support actors."
            : string.Join(" | ", friendlyForces
                .Where(force => force.VisibleInPicture)
                .Select(force => $"{force.Callsign} {force.Status}")
                .DefaultIfEmpty("Support actors currently off picture."));
        string consequenceSummary = recentIncident == null
            ? "No recent incidents."
            : $"{recentIncident.IncidentType.ToUpperInvariant()}: {recentIncident.Summary}";
        string recommendedActionSummary = selectedTrack?.RecommendedAction
            ?? objectiveStates.FirstOrDefault(objective => objective.IsUnderAttack)?.Summary
            ?? "Maintain search, sort the highest-threat raid package, and preserve fire-control discipline.";

        return new SharedOperationalPicture(
            scenarioHeader,
            scenarioNotes,
            threatSummary,
            supportSummary,
            consequenceSummary,
            recommendedActionSummary,
            threatStates,
            friendlyForces,
            objectiveStates,
            tacticalMarkers,
            selectedTrack,
            commsConsequences);
    }

    private static string BuildScenarioNotes(ScenarioDefinition? scenario, IReadOnlyList<ObjectiveTacticalState> objectives)
    {
        if (scenario == null)
            return "No scenario-loaded terrain notes available.";

        string landmarks = scenario.SectorMap.Landmarks.Count == 0
            ? "Terrain notes pending."
            : string.Join(", ", scenario.SectorMap.Landmarks
                .Take(3)
                .Select(landmark => $"{landmark.Name} ({landmark.Category})"));
        string priorityObjective = objectives.FirstOrDefault(objective => objective.IsUnderAttack || objective.IsThreatened)?.Name
            ?? objectives.FirstOrDefault()?.Name
            ?? scenario.EnemyForces.Objective;
        return $"{scenario.Description} Key terrain: {landmarks}. Priority focus: {priorityObjective}.";
    }

    private static string BuildFriendlyFireRisk(TrackFile track, SAMBattery? battery) => track.Classification switch
    {
        TrackClassification.Friendly => "Critical friendly-fire risk.",
        TrackClassification.Civilian or TrackClassification.Neutral => "Non-combatant or neutral risk. Hold fire.",
        TrackClassification.Unknown when battery?.ROE == RulesOfEngagement.WeaponsTight => "Unknown under weapons-tight discipline.",
        _ => "No elevated blue-on-blue cue."
    };

    private static string ResolveCountermeasureType(Aircraft aircraft)
    {
        if (aircraft.IsChaffActive && aircraft.IsFlareActive && aircraft.ECMActive)
            return "Chaff, flares, and ECM";
        if (aircraft.IsChaffActive && aircraft.IsFlareActive)
            return "Chaff and flares";
        if (aircraft.IsChaffActive && aircraft.ECMActive)
            return "Chaff and ECM";
        if (aircraft.IsFlareActive && aircraft.ECMActive)
            return "Flares and ECM";
        if (aircraft.IsChaffActive)
            return "Chaff";
        if (aircraft.IsFlareActive)
            return "Flares";
        if (aircraft.ECMActive)
            return "ECM";
        return "None";
    }

    private static WeaponDefinition? ResolveRecommendedWeapon(
        SimulationSnapshot snapshot,
        TrackFile track,
        out bool engagementValid,
        out string supportRequirement,
        out string reason)
    {
        supportRequirement = "No weapon recommendation.";
        reason = "no compatible shot";
        engagementValid = false;

        if (snapshot.Battery == null)
            return null;

        var best = snapshot.AvailableWeapons
            .Select(weapon =>
            {
                bool valid = CanEmployWeapon(snapshot, weapon, track, out string invalidReason, out string requirement);
                return new
                {
                    Weapon = weapon,
                    Valid = valid,
                    Requirement = requirement,
                    Reason = invalidReason,
                    Score = valid ? EstimateWeaponScore(snapshot, weapon, track) : 0.0
                };
            })
            .OrderByDescending(candidate => candidate.Valid)
            .ThenByDescending(candidate => candidate.Score)
            .FirstOrDefault();

        if (best == null)
            return null;

        engagementValid = best.Valid;
        supportRequirement = best.Requirement;
        reason = best.Reason;
        return best.Weapon;
    }

    private static bool CanEmployWeapon(
        SimulationSnapshot snapshot,
        WeaponDefinition weapon,
        TrackFile track,
        out string reason,
        out string supportRequirement)
    {
        reason = "shot valid";
        supportRequirement = weapon.RequiresRadarSupport
            ? "Radar track support required."
            : "Passive or IR employment available.";

        if (snapshot.Battery == null)
        {
            reason = "battery offline";
            supportRequirement = "Battery offline.";
            return false;
        }

        if (track.Classification is TrackClassification.Friendly or TrackClassification.Civilian or TrackClassification.Neutral)
        {
            reason = "identity check failed";
            supportRequirement = "Hold fire on non-hostile track.";
            return false;
        }

        if (track.RangeNm < weapon.MinRangeNm)
        {
            reason = "inside minimum range";
            supportRequirement = "Target is inside the weapon minimum range.";
            return false;
        }

        if (track.RangeNm > weapon.MaxRangeNm)
        {
            reason = "outside maximum range";
            supportRequirement = "Target is outside current weapon range.";
            return false;
        }

        if (track.AltitudeFt < weapon.MinAltitudeFt || track.AltitudeFt > weapon.MaxAltitudeFt)
        {
            reason = "outside altitude envelope";
            supportRequirement = "Target outside the weapon altitude envelope.";
            return false;
        }

        if (snapshot.Battery.ReadyLaunchers <= 0)
        {
            reason = "reload in progress";
            supportRequirement = "No launchers ready.";
            return false;
        }

        bool hasTrackSupport = track.IsDesignated ||
            (snapshot.Battery.RadarMode == RadarMode.TrackWhileScan && track.IsTrackHeld);
        if (weapon.RequiresRadarSupport && !hasTrackSupport)
        {
            reason = "radar support not established";
            supportRequirement = "Promote to hold or hard lock for radar-guided support.";
            return false;
        }

        if (weapon.GuidanceMode == GuidanceMode.Infrared &&
            track.AspectString.Contains("COLD", StringComparison.OrdinalIgnoreCase) &&
            track.RangeNm > 4.5)
        {
            reason = "ir seeker weak on cold aspect";
            supportRequirement = "IR seeker wants a closer or hotter aspect shot.";
            return false;
        }

        if (snapshot.Battery.ROE == RulesOfEngagement.WeaponsHold && !snapshot.Battery.IsUnderAttack)
        {
            reason = "weapons hold active";
            supportRequirement = "Command ROE currently blocks the shot.";
            return false;
        }

        if (snapshot.Battery.ROE == RulesOfEngagement.WeaponsTight &&
            track.Classification is not (TrackClassification.Hostile or TrackClassification.AssumedHostile))
        {
            reason = "roe requires hostile declaration";
            supportRequirement = "Weapons-tight discipline still needs hostile declaration.";
            return false;
        }

        return true;
    }

    private static double EstimateWeaponScore(SimulationSnapshot snapshot, WeaponDefinition weapon, TrackFile track)
    {
        double rangeFactor = (track.RangeNm - weapon.MinRangeNm) / Math.Max(0.1, weapon.MaxRangeNm - weapon.MinRangeNm);
        rangeFactor = Math.Clamp(rangeFactor, 0.0, 1.0);
        double rangeModifier = 1.0 - Math.Abs(rangeFactor - 0.4) * 0.5;
        double pk = weapon.BaseSingleShotPk * rangeModifier;

        var aircraft = track.EntityId == null
            ? null
            : snapshot.HostileAircraft.FirstOrDefault(entity => entity.Id == track.EntityId);
        if (aircraft != null)
        {
            if (weapon.SusceptibleToChaff && (aircraft.ECMActive || aircraft.IsChaffActive))
                pk *= 0.68;
            if (weapon.SusceptibleToFlares && aircraft.IsFlareActive)
                pk *= 0.62;
        }

        if (track.AltitudeFt < 500)
            pk *= 0.5;

        return Math.Clamp(pk, 0.05, 0.97);
    }

    private static ObjectiveTacticalState? ResolveClosestObjective(TrackFile track, IReadOnlyList<ObjectiveTacticalState> objectives) =>
        objectives
            .OrderBy(objective => Math.Abs(NormalizeAngle(track.BearingDeg - objective.BearingDeg)) + Math.Abs(track.RangeNm - objective.RangeNm) * 0.4)
            .FirstOrDefault();

    private static string BuildCommandCaveat(TrackFile track, SAMBattery? battery)
    {
        if (track.Classification == TrackClassification.Friendly)
            return "Friendly track. Immediate check-fire applies.";
        if (track.Classification is TrackClassification.Civilian or TrackClassification.Neutral)
            return "Civilian or neutral traffic. Command expects hold fire.";
        if (battery?.ROE == RulesOfEngagement.WeaponsHold)
            return "Weapons hold remains active unless the battery is directly under attack.";
        if (battery?.ROE == RulesOfEngagement.WeaponsTight &&
            track.Classification is not TrackClassification.Hostile and not TrackClassification.AssumedHostile)
            return "Weapons tight still requires hostile declaration.";
        return "ROE permits engagement once fire-control support is stable.";
    }

    private static string NormalizeSentenceFragment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "the shot opens";

        string normalized = text.Trim().TrimEnd('.');
        if (normalized.Length == 0)
            return "the shot opens";

        return normalized.Length == 1
            ? normalized.ToLowerInvariant()
            : char.ToLowerInvariant(normalized[0]) + normalized[1..];
    }

    private static string BuildSupportMarkerClass(FriendlySupportType type) => type switch
    {
        FriendlySupportType.CombatAirPatrol => "support-cap",
        FriendlySupportType.Awacs => "support-awacs",
        FriendlySupportType.JammingSupport => "support-jammer",
        FriendlySupportType.NearbyBattery => "support-battery",
        FriendlySupportType.SearchAndRescue => "support-sar",
        FriendlySupportType.DeclarationCell => "support-declare",
        FriendlySupportType.PictureRelay => "support-picture",
        FriendlySupportType.RelayRecovery => "support-relay",
        _ => "support"
    };

    private static string BuildSupportMissionTask(FriendlySupportPackage package) => package.Type switch
    {
        FriendlySupportType.CombatAirPatrol => "Outer-screen intercept and escort disruption.",
        FriendlySupportType.Awacs => "Wide-area picture and package sort.",
        FriendlySupportType.JammingSupport => "Escort-jam and hostile coordination disruption.",
        FriendlySupportType.NearbyBattery => "Cross-battery fire lane support.",
        FriendlySupportType.SearchAndRescue => "Downed-friendly recovery contingency.",
        FriendlySupportType.DeclarationCell => "Declare and ID support.",
        FriendlySupportType.PictureRelay => "Picture relay and fused updates.",
        FriendlySupportType.RelayRecovery => "Network restoration and comms recovery.",
        _ => package.LastSummary
    };

    private static double EstimateSupportSpeed(FriendlySupportType type) => type switch
    {
        FriendlySupportType.CombatAirPatrol => 420,
        FriendlySupportType.Awacs => 320,
        FriendlySupportType.JammingSupport => 360,
        FriendlySupportType.SearchAndRescue => 210,
        _ => 0
    };

    private static double NormalizeAngle(double degrees)
    {
        double normalized = degrees % 360;
        if (normalized > 180)
            normalized -= 360;
        if (normalized < -180)
            normalized += 360;
        return normalized;
    }
}
