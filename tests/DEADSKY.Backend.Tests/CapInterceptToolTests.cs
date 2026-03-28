using DEADSKY.AI.Client;
using DEADSKY.AI.Tools;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.EnemyAI;
using System.Text.Json;

namespace DEADSKY.Backend.Tests;

/// <summary>
/// Tests for task_cap_intercept tool in ToolRegistry.
/// Validates Task 4.1 from real-support-entities spec.
/// Requirements: 3.1, 3.2, 3.3
/// </summary>
public class CapInterceptToolTests
{
    [Fact]
    public async Task TaskCapIntercept_ValidCapAndTrack_ReturnsSuccess()
    {
        // Arrange
        using var sim = CreateSimulation();
        var toolRegistry = CreateToolRegistry(sim);

        // Spawn CAP fighter
        var capFighter = sim.Entities.SpawnFriendlyFighter(
            "VIPER 1-1",
            "SECTOR-ALPHA",
            new Vec2(50000, 30000),
            20.0);

        // Create hostile track
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 45, rangeNm: 40);
        sim.RefreshSnapshot();

        var toolCall = SimulationTestFactory.CreateToolCall("task_cap_intercept", new
        {
            cap_callsign = "VIPER 1-1",
            target_track_id = track.TrackId
        });

        // Act
        var result = await toolRegistry.ExecuteAsync(toolCall);

        // Assert
        var json = JsonDocument.Parse(result);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("VIPER 1-1", json.RootElement.GetProperty("cap_callsign").GetString());
        Assert.Equal(track.TrackId, json.RootElement.GetProperty("target_track_id").GetString());
    }

    [Fact]
    public async Task TaskCapIntercept_ValidCapAndTrack_SetsInterceptTarget()
    {
        // Arrange
        using var sim = CreateSimulation();
        var toolRegistry = CreateToolRegistry(sim);

        var capFighter = sim.Entities.SpawnFriendlyFighter(
            "VIPER 1-1",
            "SECTOR-ALPHA",
            new Vec2(50000, 30000),
            20.0);

        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 45, rangeNm: 40);
        sim.RefreshSnapshot();

        var toolCall = SimulationTestFactory.CreateToolCall("task_cap_intercept", new
        {
            cap_callsign = "VIPER 1-1",
            target_track_id = track.TrackId
        });

        // Act
        await toolRegistry.ExecuteAsync(toolCall);

        // Assert
        Assert.Equal(track.TrackId, capFighter.InterceptTargetTrackId);
    }

    [Fact]
    public async Task TaskCapIntercept_InvalidCallsign_ReturnsError()
    {
        // Arrange
        using var sim = CreateSimulation();
        var toolRegistry = CreateToolRegistry(sim);

        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 45, rangeNm: 40);
        sim.RefreshSnapshot();

        var toolCall = SimulationTestFactory.CreateToolCall("task_cap_intercept", new
        {
            cap_callsign = "NONEXISTENT 1-1",
            target_track_id = track.TrackId
        });

        // Act
        var result = await toolRegistry.ExecuteAsync(toolCall);

        // Assert
        var json = JsonDocument.Parse(result);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("not found", json.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task TaskCapIntercept_InvalidTrackId_ReturnsError()
    {
        // Arrange
        using var sim = CreateSimulation();
        var toolRegistry = CreateToolRegistry(sim);

        var capFighter = sim.Entities.SpawnFriendlyFighter(
            "VIPER 1-1",
            "SECTOR-ALPHA",
            new Vec2(50000, 30000),
            20.0);

        var toolCall = SimulationTestFactory.CreateToolCall("task_cap_intercept", new
        {
            cap_callsign = "VIPER 1-1",
            target_track_id = "INVALID-TRACK"
        });

        // Act
        var result = await toolRegistry.ExecuteAsync(toolCall);

        // Assert
        var json = JsonDocument.Parse(result);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("not found", json.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task TaskCapIntercept_CaseInsensitiveCallsign_ReturnsSuccess()
    {
        // Arrange
        using var sim = CreateSimulation();
        var toolRegistry = CreateToolRegistry(sim);

        var capFighter = sim.Entities.SpawnFriendlyFighter(
            "VIPER 1-1",
            "SECTOR-ALPHA",
            new Vec2(50000, 30000),
            20.0);

        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 45, rangeNm: 40);
        sim.RefreshSnapshot();

        var toolCall = SimulationTestFactory.CreateToolCall("task_cap_intercept", new
        {
            cap_callsign = "viper 1-1", // lowercase
            target_track_id = track.TrackId
        });

        // Act
        var result = await toolRegistry.ExecuteAsync(toolCall);

        // Assert
        var json = JsonDocument.Parse(result);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task TaskCapIntercept_InactiveFighter_ReturnsError()
    {
        // Arrange
        using var sim = CreateSimulation();
        var toolRegistry = CreateToolRegistry(sim);

        var capFighter = sim.Entities.SpawnFriendlyFighter(
            "VIPER 1-1",
            "SECTOR-ALPHA",
            new Vec2(50000, 30000),
            20.0);
        capFighter.Status = EntityStatus.Destroyed; // Make inactive

        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 45, rangeNm: 40);
        sim.RefreshSnapshot();

        var toolCall = SimulationTestFactory.CreateToolCall("task_cap_intercept", new
        {
            cap_callsign = "VIPER 1-1",
            target_track_id = track.TrackId
        });

        // Act
        var result = await toolRegistry.ExecuteAsync(toolCall);

        // Assert
        var json = JsonDocument.Parse(result);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task TaskCapIntercept_NonFighterRole_ReturnsError()
    {
        // Arrange
        using var sim = CreateSimulation();
        var toolRegistry = CreateToolRegistry(sim);

        // Spawn a bomber instead of fighter
        var bomber = Aircraft.CreateFromType("SU-24", Affiliation.Friendly);
        bomber.CallSign = "STRIKER 1-1";
        bomber.Position = new Vec2(50000, 30000);
        bomber.SyncPhysicsState();
        sim.Entities.Add(bomber);

        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 45, rangeNm: 40);
        sim.RefreshSnapshot();

        var toolCall = SimulationTestFactory.CreateToolCall("task_cap_intercept", new
        {
            cap_callsign = "STRIKER 1-1",
            target_track_id = track.TrackId
        });

        // Act
        var result = await toolRegistry.ExecuteAsync(toolCall);

        // Assert
        var json = JsonDocument.Parse(result);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
    }

    // ── Helper Methods ────────────────────────────────────────────────

    private SimulationEngine CreateSimulation()
    {
        var sim = new SimulationEngine();
        var scenario = SimulationTestFactory.CreateSingleBogeyScenario();
        var manager = new ScenarioManager(sim);
        manager.LoadScenario(scenario);
        return sim;
    }

    private ToolRegistry CreateToolRegistry(SimulationEngine sim)
    {
        var tactics = new GroupTacticManager(sim.Entities);
        return new ToolRegistry(sim, tactics, null, null);
    }
}
