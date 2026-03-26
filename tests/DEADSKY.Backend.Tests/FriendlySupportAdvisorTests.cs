using DEADSKY.Core.Campaign;

namespace DEADSKY.Backend.Tests;

public class FriendlySupportAdvisorTests
{
    [Fact]
    public void BuildTacticalUpdate_AwacsWithHostiles_SummarizesRaidAxis()
    {
        var sim = SimulationTestFactory.CreateLoadedSimulation(SimulationTestFactory.CreateOperationScenarioWithObjectives());
        SimulationTestFactory.AddDetectedHostileTrack(sim, bearingDeg: 82, rangeNm: 58);
        SimulationTestFactory.AddDetectedHostileTrack(sim, bearingDeg: 95, rangeNm: 71);
        sim.RefreshSnapshot();

        var director = new FriendlySupportDirector(sim.Comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);
        var package = director.Packages.First(p => p.Type == FriendlySupportType.Awacs);

        string? report = FriendlySupportAdvisor.BuildTacticalUpdate(package, sim.LatestSnapshot);

        Assert.NotNull(report);
        Assert.Contains("2 hostile tracks", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EAST AXIS", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildTacticalUpdate_JammerWithoutHostiles_HoldsEmissionsInReserve()
    {
        var sim = SimulationTestFactory.CreateLoadedSimulation(SimulationTestFactory.CreateOperationScenarioWithObjectives());
        sim.RefreshSnapshot();

        var director = new FriendlySupportDirector(sim.Comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);
        var package = director.Packages.First(p => p.Type == FriendlySupportType.JammingSupport);

        string? report = FriendlySupportAdvisor.BuildTacticalUpdate(package, sim.LatestSnapshot);

        Assert.NotNull(report);
        Assert.Contains("emissions in reserve", report, StringComparison.OrdinalIgnoreCase);
    }
}
