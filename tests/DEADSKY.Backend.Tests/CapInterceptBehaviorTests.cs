using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Comms;

namespace DEADSKY.Backend.Tests;

/// <summary>
/// Tests for CAP fighter intercept behavior.
/// Task 4.2: Implement CAP fighter intercept behavior
/// Requirements: 3.3, 3.4, 3.5
/// </summary>
public class CapInterceptBehaviorTests
{
    // ── Helper Methods ────────────────────────────────────────────────

    private static Aircraft CreateCapFighter(string callsign, Vec2 position)
    {
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = callsign;
        fighter.Status = EntityStatus.Active;
        fighter.Position = position;
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        
        // Set patrol sector
        var sectorCenter = CoordinateSystem.FromBearingRange(270, 50);
        fighter.PatrolSectorId = "SECTOR-ALPHA";
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = CoordinateSystem.NmToMeters(20);
        fighter.IsOnStation = true;
        
        fighter.SyncPhysicsState();
        return fighter;
    }

    private static Aircraft CreateHostileTarget(Vec2 position, Vec2 velocity)
    {
        var hostile = Aircraft.CreateFromType("MIG-29", Affiliation.Hostile);
        hostile.Position = position;
        hostile.VelocityX = velocity.X;
        hostile.VelocityY = velocity.Y;
        hostile.Status = EntityStatus.Active;
        hostile.SyncPhysicsState();
        return hostile;
    }

    // ── SetInterceptTarget Tests ──────────────────────────────────────

    [Fact]
    public void SetInterceptTarget_ValidTargetId_StoresTargetId()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));

        // Act
        fighter.SetInterceptTarget("TRACK-1234");

        // Assert
        Assert.Equal("TRACK-1234", fighter.InterceptTargetTrackId);
    }

    [Fact]
    public void SetInterceptTarget_EmptyTargetId_DoesNothing()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.InterceptTargetTrackId = "EXISTING-TARGET";

        // Act
        fighter.SetInterceptTarget("");

        // Assert
        Assert.Equal("EXISTING-TARGET", fighter.InterceptTargetTrackId);
    }

    [Fact]
    public void SetInterceptTarget_NullTargetId_DoesNothing()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.InterceptTargetTrackId = "EXISTING-TARGET";

        // Act
        fighter.SetInterceptTarget(null);

        // Assert
        Assert.Equal("EXISTING-TARGET", fighter.InterceptTargetTrackId);
    }

    [Fact]
    public void SetInterceptTarget_WithCommManager_SendsWilcoAcknowledgment()
    {
        // Arrange
        var commManager = new CommManager();
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SetInterceptTarget("TRACK-1234");
        commManager.ProcessQueue();

        // Assert - Should have sent a message
        Assert.NotNull(receivedMessage);
        Assert.Contains("WILCO", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TRACK-1234", receivedMessage.Content);
    }

    [Fact]
    public void SetInterceptTarget_ResetsTallyReport()
    {
        // Arrange
        var commManager = new CommManager();
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.CommManager = commManager;
        
        // Send tally for old target
        fighter.SendTallyReport("OLD-TARGET");
        commManager.ProcessQueue();

        // Act
        fighter.SetInterceptTarget("NEW-TARGET");

        // Assert - Should be able to send tally for new target (tally was reset)
        int tallyCount = 0;
        commManager.MessageReceived += msg =>
        {
            if (msg.Content.Contains("TALLY") && msg.Content.Contains("NEW-TARGET"))
                tallyCount++;
        };
        
        fighter.SendTallyReport("NEW-TARGET");
        commManager.ProcessQueue();
        
        Assert.Equal(1, tallyCount);
    }

    // ── SetInterceptCourse Tests ──────────────────────────────────────

    [Fact]
    public void SetInterceptCourse_StationaryTarget_SetsDirectHeading()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        Vec2 targetPosition = new Vec2(50000, 50000);
        Vec2 targetVelocity = new Vec2(0, 0); // Stationary

        // Act
        fighter.SetInterceptCourse(targetPosition, targetVelocity);

        // Assert - Should head directly toward target
        double expectedHeading = fighter.Position.HeadingTo(targetPosition);
        Assert.Equal(expectedHeading, fighter.RequestedHeadingDeg, 1.0);
    }

    [Fact]
    public void SetInterceptCourse_MovingTarget_CalculatesLeadPursuit()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        Vec2 targetPosition = new Vec2(50000, 0);
        Vec2 targetVelocity = new Vec2(0, 200); // Moving north

        // Act
        fighter.SetInterceptCourse(targetPosition, targetVelocity);

        // Assert - Should lead the target (heading should be different from direct bearing)
        double directHeading = fighter.Position.HeadingTo(targetPosition);
        Assert.NotEqual(directHeading, fighter.RequestedHeadingDeg);
        Assert.True(fighter.RequestedHeadingDeg >= 0 && fighter.RequestedHeadingDeg < 360);
    }

    [Fact]
    public void SetInterceptCourse_SetsFullSpeed()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        Vec2 targetPosition = new Vec2(50000, 50000);
        Vec2 targetVelocity = new Vec2(100, 0);

        // Act
        fighter.SetInterceptCourse(targetPosition, targetVelocity);

        // Assert
        Assert.Equal(fighter.FlightModel.MaxSpeedMps, fighter.RequestedSpeedMps);
    }

    [Fact]
    public void SetInterceptCourse_SetsEngagementAltitude()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        Vec2 targetPosition = new Vec2(50000, 50000);
        Vec2 targetVelocity = new Vec2(100, 0);

        // Act
        fighter.SetInterceptCourse(targetPosition, targetVelocity);

        // Assert - Should set standard engagement altitude (25,000 ft)
        double expectedAltitudeM = CoordinateSystem.FtToM(25000);
        Assert.Equal(expectedAltitudeM, fighter.RequestedAltitudeM);
    }

    [Fact]
    public void SetInterceptCourse_FastMovingTarget_HandlesGracefully()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        Vec2 targetPosition = new Vec2(50000, 0);
        Vec2 targetVelocity = new Vec2(0, 500); // Very fast target

        // Act
        fighter.SetInterceptCourse(targetPosition, targetVelocity);

        // Assert - Should still set valid heading and speed
        Assert.True(fighter.RequestedHeadingDeg >= 0 && fighter.RequestedHeadingDeg < 360);
        Assert.Equal(fighter.FlightModel.MaxSpeedMps, fighter.RequestedSpeedMps);
    }

    [Fact]
    public void SetInterceptCourse_TargetMovingAway_UsesDirectPursuit()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        Vec2 targetPosition = new Vec2(50000, 0);
        // Target moving away faster than fighter can catch
        Vec2 targetVelocity = new Vec2(1000, 0);

        // Act
        fighter.SetInterceptCourse(targetPosition, targetVelocity);

        // Assert - Should use direct pursuit
        double directHeading = fighter.Position.HeadingTo(targetPosition);
        Assert.Equal(directHeading, fighter.RequestedHeadingDeg, 1.0);
    }

    // ── ResumePatrol Tests ────────────────────────────────────────────

    [Fact]
    public void ResumePatrol_ClearsInterceptTarget()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.InterceptTargetTrackId = "TRACK-1234";

        // Act
        fighter.ResumePatrol();

        // Assert
        Assert.Null(fighter.InterceptTargetTrackId);
    }

    [Fact]
    public void ResumePatrol_SetsBehaviorToOrbitPatrol()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.CurrentBehavior = AircraftBehavior.IngressAttack;
        fighter.InterceptTargetTrackId = "TRACK-1234";

        // Act
        fighter.ResumePatrol();

        // Assert
        Assert.Equal(AircraftBehavior.OrbitPatrol, fighter.CurrentBehavior);
    }

    [Fact]
    public void ResumePatrol_AlreadyPatrolling_MaintainsBehavior()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        fighter.InterceptTargetTrackId = "TRACK-1234";

        // Act
        fighter.ResumePatrol();

        // Assert
        Assert.Equal(AircraftBehavior.OrbitPatrol, fighter.CurrentBehavior);
    }

    [Fact]
    public void ResumePatrol_ResetsTallyReport()
    {
        // Arrange
        var commManager = new CommManager();
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.CommManager = commManager;
        fighter.InterceptTargetTrackId = "TRACK-1234";
        
        // Send initial tally
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();

        // Act
        fighter.ResumePatrol();

        // Assert - Should be able to send tally again after resuming patrol
        int tallyCount = 0;
        commManager.MessageReceived += msg =>
        {
            if (msg.Content.Contains("TALLY"))
                tallyCount++;
        };
        
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();
        
        Assert.Equal(1, tallyCount);
    }

    // ── ExecuteCapPatrol with Intercept Tests ─────────────────────────

    [Fact]
    public void ExecuteCapPatrol_WithInterceptTarget_MaintainsFullSpeed()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.InterceptTargetTrackId = "TRACK-1234";
        fighter.RequestedSpeedMps = fighter.FlightModel.MaxSpeedMps * 0.5; // Set to half speed

        // Act
        fighter.Update(1.0);

        // Assert - Should maintain full speed during intercept
        Assert.Equal(fighter.FlightModel.MaxSpeedMps, fighter.RequestedSpeedMps);
    }

    [Fact]
    public void ExecuteCapPatrol_WithInterceptTarget_DoesNotEnforceSectorBoundary()
    {
        // Arrange
        var sectorCenter = new Vec2(50000, 30000);
        var fighter = CreateCapFighter("VIPER-1", sectorCenter);
        
        // Position fighter outside sector boundary
        Vec2 outsidePosition = sectorCenter + new Vec2(fighter.PatrolSectorRadiusM * 1.5, 0);
        fighter.Position = outsidePosition;
        fighter.HeadingDeg = 90; // Flying away from center
        fighter.SyncPhysicsState();
        
        fighter.InterceptTargetTrackId = "TRACK-1234";
        double initialHeading = fighter.HeadingDeg;

        // Act
        fighter.Update(1.0);

        // Assert - Should NOT turn back toward sector center during intercept
        // (Heading should remain unchanged or be set by external intercept course updates)
        Assert.Equal(initialHeading, fighter.HeadingDeg, 1.0);
    }

    [Fact]
    public void ExecuteCapPatrol_NoInterceptTarget_EnforcesSectorBoundary()
    {
        // Arrange
        var sectorCenter = new Vec2(50000, 30000);
        var fighter = CreateCapFighter("VIPER-1", sectorCenter);
        
        // Position fighter at edge of sector
        Vec2 edgePosition = sectorCenter + new Vec2(fighter.PatrolSectorRadiusM * 0.9, 0);
        fighter.Position = edgePosition;
        fighter.HeadingDeg = 90; // Flying away from center
        fighter.SyncPhysicsState();
        
        fighter.InterceptTargetTrackId = null; // No intercept target

        // Act
        fighter.Update(1.0);

        // Assert - Should turn toward sector center
        double headingToCenter = edgePosition.HeadingTo(sectorCenter);
        Assert.Equal(headingToCenter, fighter.RequestedHeadingDeg, 10.0); // Within 10 degrees tolerance
    }

    [Fact]
    public void ExecuteCapPatrol_NoInterceptTarget_UsesCruiseSpeed()
    {
        // Arrange
        var sectorCenter = new Vec2(50000, 30000);
        var fighter = CreateCapFighter("VIPER-1", sectorCenter);
        fighter.InterceptTargetTrackId = null;

        // Act
        fighter.Update(1.0);

        // Assert - Should use cruise speed (75% of max)
        double expectedSpeed = fighter.FlightModel.MaxSpeedMps * 0.75;
        Assert.Equal(expectedSpeed, fighter.RequestedSpeedMps, 1.0);
    }

    // ── Integration Tests ─────────────────────────────────────────────

    [Fact]
    public void InterceptWorkflow_SetTarget_UpdateCourse_ResumePatrol()
    {
        // Arrange
        var commManager = new CommManager();
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.CommManager = commManager;
        
        Vec2 targetPosition = new Vec2(50000, 50000);
        Vec2 targetVelocity = new Vec2(100, 0);

        // Act - Set intercept target
        fighter.SetInterceptTarget("TRACK-1234");
        Assert.Equal("TRACK-1234", fighter.InterceptTargetTrackId);

        // Act - Update intercept course
        fighter.SetInterceptCourse(targetPosition, targetVelocity);
        Assert.Equal(fighter.FlightModel.MaxSpeedMps, fighter.RequestedSpeedMps);

        // Act - Resume patrol after target destroyed
        fighter.ResumePatrol();
        Assert.Null(fighter.InterceptTargetTrackId);
        Assert.Equal(AircraftBehavior.OrbitPatrol, fighter.CurrentBehavior);
    }

    [Fact]
    public void InterceptWorkflow_DynamicCourseUpdates_AsTargetManeuvers()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        
        Vec2 initialTargetPosition = new Vec2(50000, 0);
        Vec2 initialTargetVelocity = new Vec2(0, 200);

        // Act - Initial intercept course
        fighter.SetInterceptTarget("TRACK-1234");
        fighter.SetInterceptCourse(initialTargetPosition, initialTargetVelocity);
        double initialHeading = fighter.RequestedHeadingDeg;

        // Act - Target maneuvers (changes velocity)
        Vec2 newTargetPosition = new Vec2(55000, 10000);
        Vec2 newTargetVelocity = new Vec2(200, 0); // Now moving east
        fighter.SetInterceptCourse(newTargetPosition, newTargetVelocity);
        double updatedHeading = fighter.RequestedHeadingDeg;

        // Assert - Heading should change as target maneuvers
        Assert.NotEqual(initialHeading, updatedHeading);
    }

    [Fact]
    public void InterceptWorkflow_TargetDestroyed_ResumesPatrol()
    {
        // Arrange
        var commManager = new CommManager();
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.CommManager = commManager;
        fighter.SetInterceptTarget("TRACK-1234");

        // Act - Simulate target destroyed
        fighter.ResumePatrol();

        // Assert
        Assert.Null(fighter.InterceptTargetTrackId);
        Assert.Equal(AircraftBehavior.OrbitPatrol, fighter.CurrentBehavior);
    }

    [Fact]
    public void InterceptWorkflow_MultipleTargets_CanSwitchTargets()
    {
        // Arrange
        var commManager = new CommManager();
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.CommManager = commManager;

        int wilcoCount = 0;
        bool hasTrack1234 = false;
        bool hasTrack5678 = false;
        
        commManager.MessageReceived += msg =>
        {
            if (msg.Content.Contains("WILCO"))
            {
                wilcoCount++;
                if (msg.Content.Contains("TRACK-1234"))
                    hasTrack1234 = true;
                if (msg.Content.Contains("TRACK-5678"))
                    hasTrack5678 = true;
            }
        };

        // Act - First target
        fighter.SetInterceptTarget("TRACK-1234");
        commManager.ProcessQueue();
        Assert.Equal("TRACK-1234", fighter.InterceptTargetTrackId);

        // Act - Switch to second target
        fighter.SetInterceptTarget("TRACK-5678");
        commManager.ProcessQueue();
        Assert.Equal("TRACK-5678", fighter.InterceptTargetTrackId);

        // Assert - Should have sent Wilco for both targets
        Assert.Equal(2, wilcoCount);
        Assert.True(hasTrack1234);
        Assert.True(hasTrack5678);
    }

    [Fact]
    public void InterceptBehavior_MaintainsFullSpeedDuringUpdate()
    {
        // Arrange
        var fighter = CreateCapFighter("VIPER-1", new Vec2(0, 0));
        fighter.InterceptTargetTrackId = "TRACK-1234";

        // Act - Multiple updates
        for (int i = 0; i < 5; i++)
        {
            fighter.Update(1.0);
        }

        // Assert - Should maintain full speed throughout
        Assert.Equal(fighter.FlightModel.MaxSpeedMps, fighter.RequestedSpeedMps);
    }
}
