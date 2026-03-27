using DEADSKY.AI.Tools;
using DEADSKY.Core.Comms;
using DEADSKY.Core.EnemyAI;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Campaign;

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

    [Fact]
    public void EnemyCommanderTools_ExposeCommanderLevelControls_WithoutLowLevelFlightMicromanagement()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var tactics = new GroupTacticManager(sim.Entities);
        var registry = new ToolRegistry(sim, tactics);

        var toolNames = registry.EnemyCommanderTools.Select(tool => tool.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("set_group_tactic", toolNames);
        Assert.Contains("get_enemy_operational_brief", toolNames);
        Assert.Contains("request_reinforcement", toolNames);
        Assert.DoesNotContain("get_shared_operational_picture", toolNames);
        Assert.DoesNotContain("request_support_action", toolNames);
        Assert.DoesNotContain("change_flight_path", toolNames);
        Assert.DoesNotContain("set_aircraft_behavior", toolNames);
        Assert.DoesNotContain("activate_ecm", toolNames);
    }

    [Fact]
    public async Task SharedOperationalPicture_Tool_ReturnsThreatSupportAndConsequenceTruth()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var support = new FriendlySupportDirector(sim.Comms);
        support.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);
        support.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP now.", 0);
        support.Tick(70, 70);

        var track = SimulationTestFactory.AddDetectedFriendlyTrack(sim);
        sim.PlayerFire(track.TrackId);

        var tactics = new GroupTacticManager(sim.Entities);
        var registry = new ToolRegistry(sim, tactics, support);

        var result = await registry.ExecuteAsync(SimulationTestFactory.CreateToolCall("get_shared_operational_picture", new { }));

        Assert.Contains("threat_summary", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("support_summary", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("friendly_fire_attempt", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VIPER", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("objectives", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnemyOperationalBrief_DoesNotLeakAlliedFusedPicture()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var hostile = SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 35, rangeNm: 18);
        sim.PlayerDesignate(hostile.TrackId);
        var tactics = new GroupTacticManager(sim.Entities);
        var registry = new ToolRegistry(sim, tactics);

        var result = await registry.ExecuteAsync(SimulationTestFactory.CreateToolCall("get_enemy_operational_brief", new { }));

        Assert.Contains("sam_pressure", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("support_summary", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("friendlies", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolAccessPolicy_RoleViews_EnforceStrictFogBoundaries()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var tactics = new GroupTacticManager(sim.Entities);
        var registry = new ToolRegistry(sim, tactics);

        var enemyEnvelope = registry.GetKnowledgeEnvelope(AgentKnowledgeRole.EnemyCommander);
        var hqEnvelope = registry.GetKnowledgeEnvelope(AgentKnowledgeRole.AlliedHQ);
        var crewEnvelope = registry.GetKnowledgeEnvelope(AgentKnowledgeRole.Crew);

        Assert.False(enemyEnvelope.HasFusedCommandPicture);
        Assert.False(enemyEnvelope.HasSupportTruth);
        Assert.True(hqEnvelope.HasFusedCommandPicture);
        Assert.True(hqEnvelope.HasSupportTruth);
        Assert.True(crewEnvelope.HasBatteryTruth);
        Assert.False(crewEnvelope.HasSupportTruth);
    }

    [Fact]
    public void ToolRegistry_GetToolsForRole_OnlyReturnsPolicyApprovedTools()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var tactics = new GroupTacticManager(sim.Entities);
        var registry = new ToolRegistry(sim, tactics);

        var crewTools = registry.GetToolsForRole(AgentKnowledgeRole.Crew).Select(tool => tool.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var intelTools = registry.GetToolsForRole(AgentKnowledgeRole.Intelligence).Select(tool => tool.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("get_battery_status", crewTools);
        Assert.DoesNotContain("request_support_action", crewTools);
        Assert.DoesNotContain("update_roe", crewTools);
        Assert.Contains("get_shared_operational_picture", intelTools);
        Assert.DoesNotContain("request_support_action", intelTools);
    }
}
