using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Campaign;

public sealed record BattleIntelSummary(
    string SectorLore,
    string ObjectiveBoard,
    string PackageSummary,
    string SectorEvent);

public static class BattleIntelDirector
{
    public static BattleIntelSummary Build(ScenarioDefinition? scenario, SimulationSnapshot snapshot, int enemyBreakthroughs = 0)
    {
        if (scenario == null)
        {
            return new BattleIntelSummary(
                "SECTOR LORE: NO ACTIVE THEATER FILE.",
                "OBJECTIVES: NONE",
                "PACKAGES: NONE DETECTED",
                "SECTOR EVENT: COMMAND AWAITING BRIEFING.");
        }

        string sectorLore = BuildSectorLore(scenario);
        string objectiveBoard = BuildObjectiveBoard(scenario, snapshot, enemyBreakthroughs);
        string packageSummary = BuildPackageSummary(scenario, snapshot);
        string sectorEvent = BuildSectorEvent(scenario, snapshot, enemyBreakthroughs);

        return new BattleIntelSummary(sectorLore, objectiveBoard, packageSummary, sectorEvent);
    }

    private static string BuildSectorLore(ScenarioDefinition scenario)
    {
        if (scenario.SectorMap.Landmarks.Count == 0)
            return $"SECTOR LORE: {scenario.SectorMap.TheaterName.ToUpperInvariant()} OPERATING PICTURE ACTIVE.";

        var featured = scenario.SectorMap.Landmarks.Take(3).Select(l => l.Name.ToUpperInvariant());
        return $"SECTOR LORE: {scenario.SectorMap.TheaterName.ToUpperInvariant()} // {string.Join(", ", featured)} SHAPE HOSTILE INGRESS.";
    }

    private static string BuildObjectiveBoard(ScenarioDefinition scenario, SimulationSnapshot snapshot, int enemyBreakthroughs)
    {
        if (scenario.SectorMap.Objectives.Count == 0)
            return $"OBJECTIVES: {scenario.EnemyForces.Objective.ToUpperInvariant()}";

        var objectives = scenario.SectorMap.Objectives
            .Select(o =>
            {
                string status = InferObjectiveStatus(scenario, o, snapshot, enemyBreakthroughs);
                return $"{o.Name.ToUpperInvariant()} {o.RangeNm:0}NM/{o.BearingDeg:000} {status}";
            });
        return $"OBJECTIVES: {string.Join(" | ", objectives)}";
    }

    private static string BuildPackageSummary(ScenarioDefinition scenario, SimulationSnapshot snapshot)
    {
        var grouped = snapshot.AllTracks
            .Where(t => t.Classification is TrackClassification.Hostile or TrackClassification.AssumedHostile or TrackClassification.Unknown)
            .GroupBy(t => InferPackageName(scenario, t))
            .OrderByDescending(g => g.Count())
            .ToList();

        if (grouped.Count == 0)
            return "PACKAGES: NO ACTIVE RAID PACKAGES TRACKED.";

        var lines = grouped.Take(3)
            .Select(g =>
            {
                var lead = g.OrderBy(t => t.RangeNm).First();
                return $"{g.Key.ToUpperInvariant()} {g.Count()} AIRFRAME {lead.RangeNm:0.0}NM";
            });

        return $"PACKAGES: {string.Join(" | ", lines)}";
    }

    private static string BuildSectorEvent(ScenarioDefinition scenario, SimulationSnapshot snapshot, int enemyBreakthroughs)
    {
        bool incoming = snapshot.AllTracks.Any(t => ContactAdvisor.Build(t).Callout == "VAMPIRE");
        bool jamming = snapshot.ActiveEcmEffects.Count > 0;
        var nearestHostile = snapshot.HostileTracks.OrderBy(t => t.RangeNm).FirstOrDefault();
        var threatenedObjective = FindThreatenedObjective(scenario, snapshot);

        if (incoming)
            return "SECTOR EVENT: VAMPIRE WARNING. DEFENDED ASSETS AT IMMEDIATE RISK.";

        if (jamming)
            return "SECTOR EVENT: ELECTRONIC INTERFERENCE BUILDING ALONG THE APPROACH AXIS.";

        if (enemyBreakthroughs > 0)
            return $"SECTOR EVENT: ENEMY PENETRATION RECORDED. {enemyBreakthroughs} BREAKTHROUGH(S) THROUGH THE INNER RING.";

        if (threatenedObjective != null)
            return $"SECTOR EVENT: {threatenedObjective.Name.ToUpperInvariant()} NOW UNDER DIRECT PRESSURE.";

        if (nearestHostile != null && nearestHostile.RangeNm < 25)
            return $"SECTOR EVENT: RAID PACKAGE PRESSING INNER RING FROM {nearestHostile.BearingDeg:000}.";

        if (snapshot.HostileTracks.Count > 0)
            return $"SECTOR EVENT: HOSTILE PROBES CONTINUE AGAINST {scenario.EnemyForces.Objective.ToUpperInvariant()}.";

        return "SECTOR EVENT: AIRSPACE TEMPORARILY STABLE. CREWS MAINTAIN WATCH.";
    }

    private static string InferPackageName(ScenarioDefinition scenario, TrackFile track)
    {
        var match = scenario.EnemyForces.Waves
            .Select((wave, index) => new
            {
                wave,
                index,
                bearingDelta = Math.Abs(NormalizeAngle(track.BearingDeg - wave.Aircraft.FirstOrDefault()?.SpawnBearingDeg ?? track.BearingDeg))
            })
            .OrderBy(entry => entry.bearingDelta)
            .FirstOrDefault();

        if (match == null)
            return "UNATTRIBUTED";

        if (!string.IsNullOrWhiteSpace(match.wave.PackageName))
            return match.wave.PackageName;

        return $"WAVE-{match.index + 1}";
    }

    private static string InferObjectiveStatus(ScenarioDefinition scenario, MapObjectiveConfig objective, SimulationSnapshot snapshot, int enemyBreakthroughs)
    {
        bool targeted = scenario.EnemyForces.Waves.Any(w => w.TargetObjectiveId.Equals(objective.Id, StringComparison.OrdinalIgnoreCase));
        int closeThreats = snapshot.HostileTracks.Count(track =>
            Math.Abs(NormalizeAngle(track.BearingDeg - objective.BearingDeg)) < 14 &&
            track.RangeNm <= objective.RangeNm + 8);
        bool shadowed = snapshot.HostileTracks.Any(track =>
            Math.Abs(NormalizeAngle(track.BearingDeg - objective.BearingDeg)) < 22 &&
            track.RangeNm <= objective.RangeNm + 20);

        if (enemyBreakthroughs > 0 && targeted)
            return "[BREACHED]";
        if (closeThreats > 0)
            return "[UNDER ATTACK]";
        if (shadowed)
            return "[SHADOWED]";
        return targeted ? "[TARGETED]" : "[WATCH]";
    }

    private static MapObjectiveConfig? FindThreatenedObjective(ScenarioDefinition scenario, SimulationSnapshot snapshot)
    {
        foreach (var objective in scenario.SectorMap.Objectives)
        {
            if (!scenario.EnemyForces.Waves.Any(w => w.TargetObjectiveId.Equals(objective.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (snapshot.HostileTracks.Any(track => Math.Abs(NormalizeAngle(track.BearingDeg - objective.BearingDeg)) < 18 && track.RangeNm <= objective.RangeNm + 12))
                return objective;
        }

        return null;
    }

    private static double NormalizeAngle(double degrees)
    {
        double normalized = degrees % 360;
        if (normalized > 180) normalized -= 360;
        if (normalized < -180) normalized += 360;
        return normalized;
    }
}
