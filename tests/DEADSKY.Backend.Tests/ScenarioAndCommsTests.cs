using DEADSKY.Core.Comms;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Backend.Tests;

public class ScenarioAndCommsTests
{
    [Fact]
    public void ScenarioManager_SpawnsConfiguredWaves_WhenTimeThresholdsAreReached()
    {
        using var sim = new SimulationEngine();
        var scenario = SimulationTestFactory.CreateMultiWaveScenario();
        var manager = new ScenarioManager(sim);

        manager.LoadScenario(scenario);
        manager.Update(7);
        Assert.Equal(2, sim.Entities.GetHostileAircraft().Count);

        manager.Update(121);
        Assert.Equal(3, sim.Entities.GetHostileAircraft().Count);
    }

    [Fact]
    public void CommManager_MarkAllRead_ClearsUnreadCount_ForChannel()
    {
        var comms = new CommManager();
        comms.SendPlayerMessage(RadioChannel.CommandNet, "ALPHA TEST");
        comms.Send(CommManager.CreateIntelMessage("Threat update"));

        Assert.Equal(1, comms.UnreadCount(RadioChannel.CommandNet));
        Assert.Equal(1, comms.UnreadCount(RadioChannel.IntelNet));

        comms.MarkAllRead(RadioChannel.CommandNet);

        Assert.Equal(0, comms.UnreadCount(RadioChannel.CommandNet));
        Assert.Equal(1, comms.UnreadCount(RadioChannel.IntelNet));
    }
}
