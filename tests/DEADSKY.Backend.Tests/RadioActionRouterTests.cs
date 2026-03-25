using DEADSKY.Core.Comms;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Backend.Tests;

public class RadioActionRouterTests
{
    [Fact]
    public void BuildFallbackReply_AirDefenseNet_ReturnsCrossBatteryResponse()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();

        var reply = RadioActionRouter.BuildFallbackReply(
            RadioChannel.AirDefenseNet,
            "This is Alpha Actual, all operation batteries acknowledge.",
            sim.LatestSnapshot,
            SimulationTestFactory.CreateOperationScenarioWithObjectives());

        Assert.NotNull(reply);
        Assert.Equal(RadioChannel.AirDefenseNet, reply!.Channel);
        Assert.Contains("BRAVO", reply.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFallbackReply_OpenFrequencyWarning_ReturnsReply()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();

        var reply = RadioActionRouter.BuildFallbackReply(
            RadioChannel.OpenFreq,
            "Unknown aircraft, turn away from this defended sector immediately.",
            sim.LatestSnapshot,
            SimulationTestFactory.CreateOperationScenarioWithObjectives());

        Assert.NotNull(reply);
        Assert.Equal(RadioChannel.OpenFreq, reply!.Channel);
        Assert.True(reply.CanReply);
    }

    [Fact]
    public void BuildFallbackReply_OpenFrequencyGenericTraffic_ReturnsUsableReply()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();

        var reply = RadioActionRouter.BuildFallbackReply(
            RadioChannel.OpenFreq,
            "Any station, radio check.",
            sim.LatestSnapshot,
            SimulationTestFactory.CreateOperationScenarioWithObjectives());

        Assert.NotNull(reply);
        Assert.Equal(RadioChannel.OpenFreq, reply!.Channel);
        Assert.Contains("UNKNOWN STATION", reply.Content, StringComparison.OrdinalIgnoreCase);
    }
}
