using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
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
    public void ScenarioManager_SpawnedAircraft_InheritMissionObjectiveAndRouteMetadata()
    {
        using var sim = new SimulationEngine();
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        var manager = new ScenarioManager(sim);

        manager.LoadScenario(scenario);
        manager.Update(7);

        var aircraft = sim.Entities.GetHostileAircraft().OfType<Aircraft>().First();
        Assert.Equal("LANCER-1", aircraft.GroupId);
        Assert.Equal("strike", aircraft.PackageRoleLabel, ignoreCase: true);
        Assert.Equal("depot", aircraft.MissionObjectiveId, ignoreCase: true);
        Assert.Equal("Kovran Depot", aircraft.MissionObjectiveName);
        Assert.Equal("SABLE GAP", aircraft.EntryLabel);
        Assert.True(aircraft.TargetWaypoint.HasValue);
        Assert.True(aircraft.ObjectivePosition.HasValue);
        Assert.Equal(AircraftBehavior.IngressAttack, aircraft.CurrentBehavior);
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

    [Fact]
    public void CommManager_SendPlayerMessage_WithRecipient_ShowsTargetInCompactDescriptor()
    {
        var comms = new CommManager();

        var message = comms.SendPlayerMessage(RadioChannel.CommandNet, "Request picture.", recipient: "ECHO ACTUAL");

        Assert.Equal("ECHO ACTUAL", message.RecipientCallsign);
        Assert.Contains("TO ECHO", message.SenderDescriptor, StringComparison.OrdinalIgnoreCase);
    }
}
