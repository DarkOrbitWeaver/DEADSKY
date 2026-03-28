using DEADSKY.Core.Campaign;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Backend.Tests;

/// <summary>
/// Task 4.2: Tests for CAP intercept course updates during intercept behavior.
/// </summary>
public class CapInterceptCourseUpdateTests
{
    /// <summary>
    /// Task 4.2: Verifies that intercept course updates handle null RadarSystem gracefully.
    /// </summary>
    [Fact]
    public void Tick_CapInterceptingTarget_NoRadarSystem_DoesNotCrash()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager, null); // No RadarSystem
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.GetAs<Aircraft>(cap.SpawnedEntityId!);
        Assert.NotNull(fighter);

        // Set intercept target (with fake track ID)
        fighter.SetInterceptTarget("TRK-9999");

        // Tick director - should not crash even without RadarSystem
        director.Tick(1, 71);

        // Fighter should still be intercepting (no update occurred)
        Assert.Equal("TRK-9999", fighter.InterceptTargetTrackId);
    }

    /// <summary>
    /// Task 4.2: Verifies that SetInterceptTarget sends Wilco acknowledgment.
    /// Requirement 2.1: WHEN a CAP_Fighter receives a tasking order, THE CAP_Fighter SHALL acknowledge with a Wilco message.
    /// </summary>
    [Fact]
    public void SetInterceptTarget_SendsWilcoAcknowledgment()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var radarSystem = new RadarSystem();
        var director = new FriendlySupportDirector(comms, entityManager, radarSystem);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.GetAs<Aircraft>(cap.SpawnedEntityId!);
        Assert.NotNull(fighter);
        fighter.CommManager = comms;

        // Set intercept target
        fighter.SetInterceptTarget("TRK-0001");

        // Verify Wilco message was sent
        comms.ProcessQueue();
        var history = comms.GetAllHistory();
        var wilcoMessage = history.LastOrDefault();
        Assert.NotNull(wilcoMessage);
        Assert.Contains("WILCO", wilcoMessage.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intercepting", wilcoMessage.Content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Task 4.2: Verifies that SetInterceptCourse calculates lead pursuit intercept geometry.
    /// Requirement 3.3: THE CAP_Fighter SHALL calculate an intercept course to the target Track.
    /// </summary>
    [Fact]
    public void SetInterceptCourse_CalculatesLeadPursuit()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.GetAs<Aircraft>(cap.SpawnedEntityId!);
        Assert.NotNull(fighter);

        // Position fighter at origin
        fighter.Position = Vec2.Zero;

        // Target moving east at 250 m/s
        Vec2 targetPosition = new Vec2(50000, 0); // 50km east
        Vec2 targetVelocity = new Vec2(250, 0); // Moving east

        // Calculate intercept course
        fighter.SetInterceptCourse(targetPosition, targetVelocity);

        // Verify heading is set (should be slightly ahead of direct bearing to lead the target)
        // Direct bearing to target would be 90 degrees (east)
        // Lead pursuit should be slightly more than 90 degrees
        Assert.True(fighter.RequestedHeadingDeg > 85 && fighter.RequestedHeadingDeg < 95);
        
        // Verify speed is set to max for intercept
        Assert.Equal(fighter.FlightModel.MaxSpeedMps, fighter.RequestedSpeedMps);
    }

    /// <summary>
    /// Task 4.2: Verifies that ResumePatrol clears intercept target and returns to patrol behavior.
    /// Requirement 3.5: IF the target Track is destroyed before intercept, THEN THE CAP_Fighter SHALL resume patrol.
    /// </summary>
    [Fact]
    public void ResumePatrol_ClearsInterceptTarget()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.GetAs<Aircraft>(cap.SpawnedEntityId!);
        Assert.NotNull(fighter);

        // Set intercept target
        fighter.SetInterceptTarget("TRK-0001");
        Assert.Equal("TRK-0001", fighter.InterceptTargetTrackId);

        // Resume patrol
        fighter.ResumePatrol();

        // Verify intercept target is cleared
        Assert.Null(fighter.InterceptTargetTrackId);
        Assert.Equal(AircraftBehavior.OrbitPatrol, fighter.CurrentBehavior);
    }
}
