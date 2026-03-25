using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Campaign;

public enum ObjectiveCondition
{
    Secure,
    Watch,
    Targeted,
    Threatened,
    UnderAttack,
    Breached
}

public sealed class ObjectiveCampaignState
{
    public string ObjectiveId { get; set; } = "";
    public string ObjectiveName { get; set; } = "";
    public string Importance { get; set; } = "primary";
    public double IntegrityPct { get; set; } = 1.0;
    public ObjectiveCondition LastCondition { get; set; } = ObjectiveCondition.Secure;
    public int TimesTargeted { get; set; }
    public int TimesBreached { get; set; }
}

public sealed class SectorCampaignState
{
    public string TheaterName { get; set; } = "";
    public int MissionsFlown { get; set; }
    public int SuccessfulDefenses { get; set; }
    public int RecordedBreakthroughs { get; set; }
    public int LastRewardModifier { get; set; }
    public string LastAfterActionSummary { get; set; } = "SECTOR STATUS: NO RECORDED ACTION.";
    public List<ObjectiveCampaignState> Objectives { get; set; } = new();
}

public sealed record SectorCampaignResolution(
    SectorCampaignState State,
    int RewardModifier,
    string IntegritySummary,
    string AfterActionSummary);

public static class SectorCampaignDirector
{
    public static SectorCampaignState EnsureScenarioState(SectorCampaignState? existing, ScenarioDefinition scenario)
    {
        var state = existing ?? new SectorCampaignState
        {
            TheaterName = scenario.SectorMap.TheaterName
        };

        state.TheaterName = scenario.SectorMap.TheaterName;

        foreach (var objective in scenario.SectorMap.Objectives)
        {
            if (state.Objectives.Any(o => o.ObjectiveId.Equals(objective.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            state.Objectives.Add(new ObjectiveCampaignState
            {
                ObjectiveId = objective.Id,
                ObjectiveName = objective.Name,
                Importance = objective.Importance
            });
        }

        return state;
    }

    public static SectorCampaignResolution ApplyMissionOutcome(
        SectorCampaignState? existing,
        ScenarioDefinition scenario,
        SimulationSnapshot snapshot,
        ScenarioManager.MissionOutcome outcome,
        int enemyBreakthroughs)
    {
        var state = EnsureScenarioState(existing, scenario);
        state.MissionsFlown++;

        double totalDamage = 0;
        foreach (var objective in scenario.SectorMap.Objectives)
        {
            var objectiveState = state.Objectives.First(o => o.ObjectiveId.Equals(objective.Id, StringComparison.OrdinalIgnoreCase));
            var condition = EvaluateCondition(scenario, objective, snapshot, enemyBreakthroughs);
            objectiveState.LastCondition = condition;
            if (condition >= ObjectiveCondition.Targeted)
                objectiveState.TimesTargeted++;
            if (condition == ObjectiveCondition.Breached)
            {
                objectiveState.TimesBreached++;
                state.RecordedBreakthroughs++;
            }

            double damage = CalculateDamage(objective, condition, outcome);
            if (damage > 0)
            {
                objectiveState.IntegrityPct = Math.Clamp(objectiveState.IntegrityPct - damage, 0, 1);
                totalDamage += damage;
            }
        }

        if (enemyBreakthroughs == 0 && outcome != ScenarioManager.MissionOutcome.Defeat)
            state.SuccessfulDefenses++;

        int rewardModifier = CalculateRewardModifier(state, totalDamage, outcome, enemyBreakthroughs);
        state.LastRewardModifier = rewardModifier;
        state.LastAfterActionSummary = BuildAfterActionSummary(state, outcome, enemyBreakthroughs);

        return new SectorCampaignResolution(
            state,
            rewardModifier,
            BuildIntegritySummary(state),
            state.LastAfterActionSummary);
    }

    public static string BuildIntegritySummary(SectorCampaignState? state)
    {
        if (state == null || state.Objectives.Count == 0)
            return "SECTOR STATUS: NO OBJECTIVE STATE AVAILABLE.";

        var lines = state.Objectives
            .OrderByDescending(o => ImportanceWeight(o.Importance))
            .Select(o => $"{o.ObjectiveName.ToUpperInvariant()} {o.IntegrityPct:P0} [{FormatCondition(o.LastCondition)}]")
            .ToList();

        return $"SECTOR STATUS: {string.Join(" | ", lines)}";
    }

    private static string BuildAfterActionSummary(SectorCampaignState state, ScenarioManager.MissionOutcome outcome, int enemyBreakthroughs)
    {
        var worst = state.Objectives
            .OrderBy(o => o.IntegrityPct)
            .ThenByDescending(o => ImportanceWeight(o.Importance))
            .FirstOrDefault();

        if (worst == null)
            return "AFTER ACTION: NO OBJECTIVE IMPACT RECORDED.";

        if (enemyBreakthroughs > 0 || worst.LastCondition == ObjectiveCondition.Breached)
        {
            return $"AFTER ACTION: {worst.ObjectiveName.ToUpperInvariant()} SUFFERED A BREACH. REPAIR PRIORITY ELEVATED.";
        }

        if (worst.LastCondition == ObjectiveCondition.UnderAttack || worst.LastCondition == ObjectiveCondition.Threatened)
        {
            return $"AFTER ACTION: {worst.ObjectiveName.ToUpperInvariant()} HELD UNDER PRESSURE. ENGINEERS REPORT REDUCED READINESS.";
        }

        return outcome switch
        {
            ScenarioManager.MissionOutcome.Victory => "AFTER ACTION: DEFENDED ASSETS REMAIN OPERATIONAL. THEATER CONFIDENCE IMPROVING.",
            ScenarioManager.MissionOutcome.PartialVictory => "AFTER ACTION: THEATER HELD WITH MINOR DISRUPTION. COMMAND REQUESTS TIGHTER COVERAGE.",
            _ => $"AFTER ACTION: {worst.ObjectiveName.ToUpperInvariant()} STATUS DEGRADED. COMMAND REASSESSING COVERAGE.",
        };
    }

    private static int CalculateRewardModifier(SectorCampaignState state, double totalDamage, ScenarioManager.MissionOutcome outcome, int enemyBreakthroughs)
    {
        int modifier = 0;
        if (enemyBreakthroughs == 0 && totalDamage <= 0.02 && outcome == ScenarioManager.MissionOutcome.Victory)
            modifier += 1200;
        else if (enemyBreakthroughs == 0 && outcome != ScenarioManager.MissionOutcome.Defeat)
            modifier += 450;

        modifier -= (int)Math.Round(totalDamage * 6000);
        modifier -= enemyBreakthroughs * 900;

        if (state.Objectives.Any(o => ImportanceWeight(o.Importance) >= 1.3 && o.IntegrityPct < 0.75))
            modifier -= 700;

        return modifier;
    }

    private static double CalculateDamage(MapObjectiveConfig objective, ObjectiveCondition condition, ScenarioManager.MissionOutcome outcome)
    {
        double baseDamage = condition switch
        {
            ObjectiveCondition.Secure => 0,
            ObjectiveCondition.Watch => 0,
            ObjectiveCondition.Targeted => 0,
            ObjectiveCondition.Threatened => 0.03,
            ObjectiveCondition.UnderAttack => 0.08,
            ObjectiveCondition.Breached => 0.18,
            _ => 0
        };

        if (outcome == ScenarioManager.MissionOutcome.Defeat && condition >= ObjectiveCondition.UnderAttack)
            baseDamage += 0.05;

        return baseDamage * ImportanceWeight(objective.Importance);
    }

    private static ObjectiveCondition EvaluateCondition(
        ScenarioDefinition scenario,
        MapObjectiveConfig objective,
        SimulationSnapshot snapshot,
        int enemyBreakthroughs)
    {
        bool targeted = scenario.EnemyForces.Waves.Any(w => w.TargetObjectiveId.Equals(objective.Id, StringComparison.OrdinalIgnoreCase));
        bool closeThreat = snapshot.HostileTracks.Any(track =>
            Math.Abs(NormalizeAngle(track.BearingDeg - objective.BearingDeg)) < 14 &&
            track.RangeNm <= objective.RangeNm + 8);
        bool shadowed = snapshot.HostileTracks.Any(track =>
            Math.Abs(NormalizeAngle(track.BearingDeg - objective.BearingDeg)) < 22 &&
            track.RangeNm <= objective.RangeNm + 20);

        if (enemyBreakthroughs > 0 && targeted)
            return ObjectiveCondition.Breached;
        if (closeThreat)
            return ObjectiveCondition.UnderAttack;
        if (shadowed)
            return ObjectiveCondition.Threatened;
        if (targeted)
            return ObjectiveCondition.Targeted;
        return snapshot.HostileTracks.Count > 0 ? ObjectiveCondition.Watch : ObjectiveCondition.Secure;
    }

    private static double ImportanceWeight(string importance) => importance.ToLowerInvariant() switch
    {
        "primary" => 1.4,
        "critical" => 1.6,
        _ => 1.0
    };

    private static string FormatCondition(ObjectiveCondition condition) => condition switch
    {
        ObjectiveCondition.UnderAttack => "UNDER ATTACK",
        ObjectiveCondition.Targeted => "TARGETED",
        _ => condition.ToString().ToUpperInvariant()
    };

    private static double NormalizeAngle(double degrees)
    {
        double normalized = degrees % 360;
        if (normalized > 180) normalized -= 360;
        if (normalized < -180) normalized += 360;
        return normalized;
    }
}
