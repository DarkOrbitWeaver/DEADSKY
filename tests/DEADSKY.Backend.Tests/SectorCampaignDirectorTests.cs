using DEADSKY.Core.Campaign;
using DEADSKY.Core.Scenario;

namespace DEADSKY.Backend.Tests;

public class SectorCampaignDirectorTests
{
    [Fact]
    public void ApplyMissionOutcome_RewardsCleanDefense()
    {
        var scenario = SimulationTestFactory.CreateSingleBogeyScenario();
        scenario.SectorMap = new SectorMapConfig
        {
            TheaterName = "Kovran Lowlands",
            Objectives = new List<MapObjectiveConfig>
            {
                new() { Id = "airfield", Name = "Kovran Airfield", BearingDeg = 132, RangeNm = 62, Importance = "primary" }
            }
        };
        scenario.EnemyForces.Waves[0].TargetObjectiveId = "airfield";

        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        var resolution = SectorCampaignDirector.ApplyMissionOutcome(
            existing: null,
            scenario,
            sim.LatestSnapshot,
            ScenarioManager.MissionOutcome.Victory,
            enemyBreakthroughs: 0);

        Assert.True(resolution.RewardModifier > 0);
        Assert.Contains("AIRFIELD", resolution.IntegritySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1.0, resolution.State.Objectives[0].IntegrityPct, 3);
    }

    [Fact]
    public void ApplyMissionOutcome_ReducesIntegrity_WhenObjectiveIsBreached()
    {
        var scenario = SimulationTestFactory.CreateSingleBogeyScenario();
        scenario.SectorMap = new SectorMapConfig
        {
            TheaterName = "Kovran Lowlands",
            Objectives = new List<MapObjectiveConfig>
            {
                new() { Id = "airfield", Name = "Kovran Airfield", BearingDeg = 45, RangeNm = 20, Importance = "primary" }
            }
        };
        scenario.EnemyForces.Waves[0].TargetObjectiveId = "airfield";

        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 45, rangeNm: 18);

        var resolution = SectorCampaignDirector.ApplyMissionOutcome(
            existing: null,
            scenario,
            sim.LatestSnapshot,
            ScenarioManager.MissionOutcome.Defeat,
            enemyBreakthroughs: 1);

        Assert.True(resolution.RewardModifier < 0);
        Assert.True(resolution.State.Objectives[0].IntegrityPct < 0.8);
        Assert.Equal(ObjectiveCondition.Breached, resolution.State.Objectives[0].LastCondition);
        Assert.Contains("BREACH", resolution.AfterActionSummary, StringComparison.OrdinalIgnoreCase);
    }
}
