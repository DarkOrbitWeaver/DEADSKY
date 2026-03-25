using DEADSKY.AI.Tools;
using DEADSKY.Core.Comms;
using DEADSKY.Core.EnemyAI;
using DEADSKY.Core.Entities;

namespace DEADSKY.Backend.Tests;

public class ToolRegistryTests
{
    [Fact]
    public async Task UpdateRoe_Tool_ChangesBatteryState_AndBroadcastsCommandMessage()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var tactics = new GroupTacticManager(sim.Entities);
        var registry = new ToolRegistry(sim, tactics);

        var result = await registry.ExecuteAsync(SimulationTestFactory.CreateToolCall("update_roe", new
        {
            roe = "weapons_free",
            reason = "Hostile act confirmed",
            authority = "ECHO ACTUAL"
        }));

        var battery = sim.Entities.GetPlayerBattery()!;
        var roeMessage = SimulationTestFactory.DrainSingleQueuedMessage(sim.Comms);

        Assert.Contains("WeaponsFree", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RulesOfEngagement.WeaponsFree, battery.ROE);
        Assert.Equal("ECHO ACTUAL", roeMessage.SenderCallsign);
        Assert.Contains("ROE CHANGE", roeMessage.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendRadioMessage_Tool_QueuesMessage_OnRequestedChannel()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var tactics = new GroupTacticManager(sim.Entities);
        var registry = new ToolRegistry(sim, tactics);

        await registry.ExecuteAsync(SimulationTestFactory.CreateToolCall("send_radio_message", new
        {
            channel = "intel",
            sender_callsign = "INTEL-1",
            message = "Probable strike package forming east.",
            priority = "priority"
        }));

        var intelMessage = SimulationTestFactory.DrainSingleQueuedMessage(sim.Comms);

        Assert.Equal(RadioChannel.IntelNet, intelMessage.Channel);
        Assert.Equal("INTEL-1", intelMessage.SenderCallsign);
        Assert.Equal(MessagePriority.Priority, intelMessage.Priority);
    }
}
