using DEADSKY.Core.Campaign;
using DEADSKY.Core.Personnel;

namespace DEADSKY.Backend.Tests;

public class MissionAdvisorTests
{
    [Fact]
    public void Build_ReturnsDeclareRecommendation_ForUnknownTrackUnderWeaponsTight()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim);
        track.Classification = DEADSKY.Core.Radar.TrackClassification.Unknown;

        var assessment = MissionAdvisor.Build(
            sim.LatestSnapshot,
            SimulationTestFactory.CreateSingleBogeyScenario(),
            enemyBreakthroughs: 0,
            selectedTrack: track,
            sim.Weapons);

        Assert.Contains("REQUEST DECLARE", assessment.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeNextWave_ReturnsEta_ForUpcomingWave()
    {
        var scenario = SimulationTestFactory.CreateMultiWaveScenario();

        var result = MissionAdvisor.DescribeNextWave(scenario, 30);

        Assert.Contains("ETA", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AIRFRAME", result, StringComparison.OrdinalIgnoreCase);
    }
}
