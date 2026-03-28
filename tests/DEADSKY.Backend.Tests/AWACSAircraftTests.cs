using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;

namespace DEADSKY.Backend.Tests;

public class AWACSAircraftTests
{
    [Fact]
    public void AWACSAircraft_Constructor_SetsE3SentryCharacteristics()
    {
        // Arrange & Act
        var awacs = new AWACSAircraft();

        // Assert - Physical characteristics
        Assert.Equal(530, awacs.MaxSpeedKts);
        Assert.Equal(460, awacs.CruiseSpeedKts);
        Assert.Equal(30000, awacs.CruiseAltitudeFt);
        Assert.Equal(42000, awacs.MaxAltitudeFt);
        Assert.Equal(1.5, awacs.TurnRateDegreesPerSec);
    }

    [Fact]
    public void AWACSAircraft_Constructor_SetsCorrectEntityType()
    {
        // Arrange & Act
        var awacs = new AWACSAircraft();

        // Assert
        Assert.Equal(EntityType.Aircraft, awacs.Type);
        Assert.Equal(Affiliation.Friendly, awacs.Affiliation);
    }

    [Fact]
    public void AWACSAircraft_Constructor_SetsCorrectDesignation()
    {
        // Arrange & Act
        var awacs = new AWACSAircraft();

        // Assert
        Assert.Equal("E-3 SENTRY AWACS", awacs.Designation);
    }

    [Fact]
    public void AWACSAircraft_Constructor_SetsRealisticRCS()
    {
        // Arrange & Act
        var awacs = new AWACSAircraft();

        // Assert - Large aircraft should have high RCS
        Assert.Equal(40.0, awacs.RcsM2);
    }

    [Fact]
    public void AWACSAircraft_Constructor_SetsRadarRangeMultiplier()
    {
        // Arrange & Act
        var awacs = new AWACSAircraft();

        // Assert - Default 30% boost
        Assert.Equal(1.3, awacs.RadarRangeMultiplier);
    }

    [Fact]
    public void AWACSAircraft_Constructor_InitializesRacetrackProperties()
    {
        // Arrange & Act
        var awacs = new AWACSAircraft();

        // Assert - Properties should be initialized (default to 0)
        Assert.Equal(0, awacs.RacetrackLegLengthNm);
        Assert.Equal(0, awacs.RacetrackHeadingDeg);
        Assert.Equal(0, awacs.RacetrackTurnRadiusNm);
        Assert.Equal(0, awacs.RadarCoverageRadiusNm);
    }

    [Fact]
    public void AWACSAircraft_Constructor_SetsLongEnduranceFuelCharacteristics()
    {
        // Arrange & Act
        var awacs = new AWACSAircraft();

        // Assert - AWACS should have large fuel capacity for long missions
        Assert.Equal(118000, awacs.FuelCapacityKg);
        Assert.Equal(3.5, awacs.FuelBurnRateKgSec);
        Assert.Equal(20000, awacs.BingoFuelKg);
    }

    [Fact]
    public void AWACSAircraft_RacetrackProperties_CanBeSet()
    {
        // Arrange
        var awacs = new AWACSAircraft();

        // Act
        awacs.RacetrackLegLengthNm = 20.0;
        awacs.RacetrackHeadingDeg = 90.0;
        awacs.RacetrackTurnRadiusNm = 5.0;
        awacs.RadarCoverageRadiusNm = 200.0;

        // Assert
        Assert.Equal(20.0, awacs.RacetrackLegLengthNm);
        Assert.Equal(90.0, awacs.RacetrackHeadingDeg);
        Assert.Equal(5.0, awacs.RacetrackTurnRadiusNm);
        Assert.Equal(200.0, awacs.RadarCoverageRadiusNm);
    }

    [Fact]
    public void AWACSAircraft_RadarRangeMultiplier_CanBeModified()
    {
        // Arrange
        var awacs = new AWACSAircraft();

        // Act
        awacs.RadarRangeMultiplier = 1.5; // 50% boost

        // Assert
        Assert.Equal(1.5, awacs.RadarRangeMultiplier);
    }

    [Fact]
    public void AWACSAircraft_InheritsFromAircraft()
    {
        // Arrange & Act
        var awacs = new AWACSAircraft();

        // Assert
        Assert.IsAssignableFrom<Aircraft>(awacs);
    }

    [Fact]
    public void AWACSAircraft_Constructor_InitializesRequestedStateTocruise()
    {
        // Arrange & Act
        var awacs = new AWACSAircraft();

        // Assert
        Assert.Equal(CoordinateSystem.FtToM(30000), awacs.RequestedAltitudeM);
        Assert.Equal(CoordinateSystem.KtsToMps(460), awacs.RequestedSpeedMps);
    }

    [Fact]
    public void AWACSAircraft_ExecuteAwacsOrbit_MaintainsCruiseAltitudeAndSpeed()
    {
        // Arrange
        var awacs = new AWACSAircraft
        {
            RacetrackLegLengthNm = 20.0,
            RacetrackHeadingDeg = 90.0,
            RacetrackTurnRadiusNm = 5.0,
            Position = new Vec2(0, 0),
            HeadingDeg = 90.0,
            AltitudeM = CoordinateSystem.FtToM(30000),
            SpeedMps = CoordinateSystem.KtsToMps(460),
            FuelRemainingKg = 100000 // Plenty of fuel
        };
        awacs.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        awacs.SyncPhysicsState();

        // Act
        awacs.Update(1.0);

        // Assert - Should request cruise altitude and speed
        Assert.Equal(CoordinateSystem.FtToM(30000), awacs.RequestedAltitudeM);
        Assert.Equal(CoordinateSystem.KtsToMps(460), awacs.RequestedSpeedMps);
    }

    [Fact]
    public void AWACSAircraft_ExecuteAwacsOrbit_FliesRacetrackHeading()
    {
        // Arrange
        var awacs = new AWACSAircraft
        {
            RacetrackLegLengthNm = 20.0,
            RacetrackHeadingDeg = 90.0,
            RacetrackTurnRadiusNm = 5.0,
            Position = new Vec2(0, 0),
            HeadingDeg = 90.0,
            AltitudeM = CoordinateSystem.FtToM(30000),
            SpeedMps = CoordinateSystem.KtsToMps(460),
            FuelRemainingKg = 100000 // Plenty of fuel
        };
        awacs.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        awacs.SyncPhysicsState();

        // Act
        awacs.Update(1.0);

        // Assert - Should fly at racetrack heading (90 degrees) with small tolerance for physics
        // The aircraft starts at position (0,0) which becomes the racetrack center
        // On first leg, it flies at the racetrack heading (90 degrees)
        Assert.InRange(awacs.RequestedHeadingDeg, 85.0, 95.0);
    }

    [Fact]
    public void AWACSAircraft_ExecuteAwacsOrbit_WithoutConfiguration_FallsBackToSimpleOrbit()
    {
        // Arrange
        var awacs = new AWACSAircraft
        {
            Position = new Vec2(0, 0),
            HeadingDeg = 45.0,
            AltitudeM = CoordinateSystem.FtToM(30000),
            SpeedMps = CoordinateSystem.KtsToMps(460),
            FuelRemainingKg = 100000 // Plenty of fuel
        };
        awacs.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        awacs.SyncPhysicsState();

        double initialHeading = awacs.HeadingDeg;

        // Act
        awacs.Update(1.0);

        // Assert - Should turn gradually (simple orbit fallback)
        Assert.NotEqual(initialHeading, awacs.RequestedHeadingDeg);
        Assert.Equal(CoordinateSystem.FtToM(30000), awacs.RequestedAltitudeM);
        Assert.Equal(CoordinateSystem.KtsToMps(460), awacs.RequestedSpeedMps);
    }

    [Fact]
    public void AWACSAircraft_ExecuteAwacsOrbit_CompletesFullRacetrackPattern()
    {
        // Arrange
        var awacs = new AWACSAircraft
        {
            RacetrackLegLengthNm = 10.0, // Shorter for faster test
            RacetrackHeadingDeg = 0.0,   // North
            RacetrackTurnRadiusNm = 2.0,
            Position = new Vec2(0, 0),
            HeadingDeg = 0.0,
            AltitudeM = CoordinateSystem.FtToM(30000),
            SpeedMps = CoordinateSystem.KtsToMps(460),
            FuelRemainingKg = 100000 // Plenty of fuel
        };
        awacs.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        awacs.SyncPhysicsState();

        Vec2 startPosition = awacs.Position;

        // Act - Simulate multiple updates to complete pattern
        for (int i = 0; i < 100; i++)
        {
            awacs.Update(10.0); // 10 second steps
        }

        // Assert - Should request altitude and speed throughout
        Assert.Equal(CoordinateSystem.FtToM(30000), awacs.RequestedAltitudeM);
        Assert.Equal(CoordinateSystem.KtsToMps(460), awacs.RequestedSpeedMps);
        // Position should have changed (aircraft is moving)
        Assert.NotEqual(startPosition, awacs.Position);
    }

    [Fact]
    public void AWACSAircraft_Update_OnlyExecutesOrbitWhenInOrbitPatrolBehavior()
    {
        // Arrange
        var awacs = new AWACSAircraft
        {
            RacetrackLegLengthNm = 20.0,
            RacetrackHeadingDeg = 90.0,
            RacetrackTurnRadiusNm = 5.0,
            Position = new Vec2(0, 0),
            HeadingDeg = 90.0,
            AltitudeM = CoordinateSystem.FtToM(30000),
            SpeedMps = CoordinateSystem.KtsToMps(460),
            FuelRemainingKg = 100000 // Plenty of fuel
        };
        awacs.CurrentBehavior = AircraftBehavior.IngressAttack; // Different behavior
        awacs.SyncPhysicsState();

        // Act
        awacs.Update(1.0);

        // Assert - Should maintain behavior (base class handles IngressAttack)
        // Note: Behavior might change due to fuel or other factors in base class
        Assert.True(awacs.CurrentBehavior == AircraftBehavior.IngressAttack || 
                    awacs.CurrentBehavior == AircraftBehavior.EgressRetreat);
    }
}
