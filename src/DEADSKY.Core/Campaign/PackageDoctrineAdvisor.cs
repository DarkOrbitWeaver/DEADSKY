using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;

namespace DEADSKY.Core.Campaign;

public sealed record PackageDoctrineAdvisory(
    string PackageLabel,
    string RoleLabel,
    string RouteLabel,
    string ObjectiveLabel,
    string DoctrineLabel);

public static class PackageDoctrineAdvisor
{
    public static PackageDoctrineAdvisory Build(ScenarioDefinition? scenario, TrackFile? track)
    {
        if (track == null)
        {
            return new PackageDoctrineAdvisory(
                "PACKAGE: NONE",
                "ROLE: STANDBY",
                "ROUTE: STANDBY",
                "OBJECTIVE: STANDBY",
                "DOCTRINE: NO TRACK SELECTED.");
        }

        var wave = scenario?.EnemyForces.Waves.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.PackageName) &&
            candidate.PackageName.Equals(track.GroupLabel, StringComparison.OrdinalIgnoreCase));

        string packageLabel = string.IsNullOrWhiteSpace(track.GroupLabel)
            ? "UNATTRIBUTED"
            : track.GroupLabel.ToUpperInvariant();
        string roleLabel = BuildRoleLabel(wave, track);
        string routeLabel = string.IsNullOrWhiteSpace(wave?.EntryLabel)
            ? "UNSPECIFIED AXIS"
            : wave.EntryLabel.ToUpperInvariant();
        string objectiveLabel = BuildObjectiveLabel(scenario, wave);
        string doctrine = BuildDoctrineLine(wave, track);

        return new PackageDoctrineAdvisory(
            $"PACKAGE: {packageLabel}",
            $"ROLE: {roleLabel}",
            $"ROUTE: {routeLabel}",
            $"OBJECTIVE: {objectiveLabel}",
            $"DOCTRINE: {doctrine}");
    }

    private static string BuildRoleLabel(WaveConfig? wave, TrackFile track)
    {
        if (!string.IsNullOrWhiteSpace(wave?.PackageRole))
            return wave.PackageRole.Replace('_', ' ').ToUpperInvariant();

        string designation = track.TrackDesignation.ToUpperInvariant();
        if (designation.Contains("MISSILE", StringComparison.Ordinal) || designation.Contains("CRUISE", StringComparison.Ordinal))
            return "MISSILE STRIKE";
        if (designation.Contains("MQ-", StringComparison.Ordinal) || designation.Contains("DRONE", StringComparison.Ordinal))
            return "RECON / FEINT";
        if (designation.Contains("SU-24", StringComparison.Ordinal) || designation.Contains("SU-25", StringComparison.Ordinal))
            return "STRIKE";
        if (designation.Contains("MIG-", StringComparison.Ordinal) || designation.Contains("F-", StringComparison.Ordinal))
            return "ESCORT / FIGHTER";
        return "UNKNOWN PACKAGE";
    }

    private static string BuildObjectiveLabel(ScenarioDefinition? scenario, WaveConfig? wave)
    {
        if (scenario == null)
            return "UNKNOWN";

        if (wave != null && !string.IsNullOrWhiteSpace(wave.TargetObjectiveId))
        {
            var objective = scenario.SectorMap.Objectives.FirstOrDefault(candidate =>
                candidate.Id.Equals(wave.TargetObjectiveId, StringComparison.OrdinalIgnoreCase));
            if (objective != null)
                return objective.Name.ToUpperInvariant();
        }

        return string.IsNullOrWhiteSpace(scenario.EnemyForces.Objective)
            ? "UNKNOWN"
            : scenario.EnemyForces.Objective.ToUpperInvariant();
    }

    private static string BuildDoctrineLine(WaveConfig? wave, TrackFile track)
    {
        string role = wave?.PackageRole?.ToLowerInvariant() ?? "";
        if (role.Contains("jammer", StringComparison.Ordinal))
            return "EXPECT ECM SUPPORT AND STANDOFF PRESSURE.";
        if (role.Contains("feint", StringComparison.Ordinal))
            return "LIKELY SPLIT-AXIS FEINT TO PULL SHOTS OFF MAIN RAID.";
        if (role.Contains("fighter", StringComparison.Ordinal) || role.Contains("escort", StringComparison.Ordinal))
            return "SCREEN PACKAGE PROBABLY COVERING STRIKERS BEHIND IT.";
        if (role.Contains("strike", StringComparison.Ordinal))
            return "DIRECT ATTACK PACKAGE. PRIORITIZE BEFORE INNER-RING PENETRATION.";

        if (track.ThreatLevel >= 0.7 && track.IsHot)
            return "HIGH-THREAT INGRESS. PREPARE RAPID DESIGNATE / ENGAGE FLOW.";
        if (track.IsHot)
            return "CLOSING CONTACT. HOLD TRACK QUALITY AND TIME THE SHOT.";
        return "MANEUVERING CONTACT. WATCH FOR RE-ORIENT OR SUPPORT ROLE.";
    }
}
