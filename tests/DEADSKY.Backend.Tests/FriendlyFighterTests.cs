using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;

namespace DEADSKY.Backend.Tests;

/// <summary>
/// Tests for friendly fighter (CAP) specific properties and behavior.
/// Validates Requirements 1.2 and 1.10 from real-support-entities spec.
/// </summary>
public class FriendlyFighterTests
{
    [Fact]
    public void Aircraft_DefaultLoadout_HasCorrectWeaponCounts()
    {
        // Arrange & Act
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;

        // Assert
        Assert.Equal(4, fighter.Aim120Count);
        Assert.Equal(2, fighter.Aim9Count);
        Assert.False(fighter.IsWinchester);
    }

    [Fact]
    public void Aircraft_NoWeapons_IsWinchester()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        
        // Act
        fighter.Aim120Count = 0;
        fighter.Aim9Count = 0;

        // Assert
        Assert.True(fighter.IsWinchester);
    }

    [Fact]
    public void Aircraft_PartialWeapons_NotWinchester()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        
        // Act
        fighter.Aim120Count = 2;
        fighter.Aim9Count = 0;

        // Assert
        Assert.False(fighter.IsWinchester);
    }

    [Fact]
    public void Aircraft_PatrolSectorAssignment_StoresCorrectly()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);

        // Act
        fighter.PatrolSectorId = "SECTOR-ALPHA";
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;

        // Assert
        Assert.Equal("SECTOR-ALPHA", fighter.PatrolSectorId);
        Assert.Equal(sectorCenter, fighter.PatrolSectorCenter);
        Assert.Equal(sectorRadius, fighter.PatrolSectorRadiusM);
    }

    [Fact]
    public void Aircraft_InterceptTasking_StoresTargetTrackId()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);

        // Act
        fighter.InterceptTargetTrackId = "TRACK-1234";

        // Assert
        Assert.Equal("TRACK-1234", fighter.InterceptTargetTrackId);
    }

    [Fact]
    public void Aircraft_OnStationStatus_TracksArrivalTime()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var arrivalTime = DateTime.UtcNow;

        // Act
        fighter.IsOnStation = true;
        fighter.OnStationTime = arrivalTime;

        // Assert
        Assert.True(fighter.IsOnStation);
        Assert.Equal(arrivalTime, fighter.OnStationTime);
    }

    [Fact]
    public void Aircraft_BingoFuel_IsOnRTBReturnsTrue()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        
        // Act
        fighter.FuelRemainingKg = fighter.BingoFuelKg - 100; // Below bingo

        // Assert
        Assert.True(fighter.IsOnRTB);
    }

    [Fact]
    public void Aircraft_AboveBingoFuel_IsOnRTBReturnsFalse()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        
        // Act
        fighter.FuelRemainingKg = fighter.BingoFuelKg + 500; // Above bingo

        // Assert
        Assert.False(fighter.IsOnRTB);
    }

    [Fact]
    public void Aircraft_Update_BurnsFuelOverTime()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Position = CoordinateSystem.FromBearingRange(45, 30);
        fighter.SyncPhysicsState();
        double initialFuel = fighter.FuelRemainingKg;
        double deltaTime = 1.0; // 1 second

        // Act
        fighter.Update(deltaTime);

        // Assert
        Assert.True(fighter.FuelRemainingKg < initialFuel);
        double expectedFuelBurned = fighter.FuelBurnRateKgSec * deltaTime;
        Assert.Equal(initialFuel - expectedFuelBurned, fighter.FuelRemainingKg, 0.01);
    }

    [Fact]
    public void Aircraft_ReachesBingoFuel_TransitionsToEgress()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Position = CoordinateSystem.FromBearingRange(45, 30);
        fighter.SyncPhysicsState();
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        fighter.FuelRemainingKg = fighter.BingoFuelKg + 10; // Just above bingo

        // Act
        fighter.Update(10.0); // Burn enough fuel to go below bingo

        // Assert
        Assert.True(fighter.IsOnRTB);
        Assert.Equal(AircraftBehavior.EgressRetreat, fighter.CurrentBehavior);
    }

    [Fact]
    public void Aircraft_WeaponExpenditure_DecreasesCount()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;

        // Act - Simulate weapon expenditure
        fighter.Aim120Count--;
        fighter.Aim9Count--;

        // Assert
        Assert.Equal(3, fighter.Aim120Count);
        Assert.Equal(1, fighter.Aim9Count);
        Assert.False(fighter.IsWinchester);
    }

    [Fact]
    public void Aircraft_AllWeaponsExpended_BecomesWinchester()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 1;
        fighter.Aim9Count = 1;

        // Act - Expend all weapons
        fighter.Aim120Count = 0;
        fighter.Aim9Count = 0;

        // Assert
        Assert.True(fighter.IsWinchester);
    }

    // ── CAP Patrol Behavior Tests (Task 1.2) ──────────────────────────

    [Fact]
    public void Aircraft_OrbitPatrol_WithSectorAssignment_ExecutesCapPatrol()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        fighter.Position = sectorCenter; // Start at center
        fighter.SyncPhysicsState();

        // Act
        fighter.Update(1.0);

        // Assert - Should maintain patrol behavior and set appropriate speed/altitude
        Assert.Equal(AircraftBehavior.OrbitPatrol, fighter.CurrentBehavior);
        Assert.True(fighter.RequestedAltitudeM > 0);
        Assert.True(fighter.RequestedSpeedMps > 0);
    }

    [Fact]
    public void Aircraft_CapPatrol_SetsOnStationWhenEnteringSector()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        fighter.Position = sectorCenter; // Start at center (within sector)
        fighter.IsOnStation = false;
        fighter.SyncPhysicsState();

        // Act
        fighter.Update(1.0);

        // Assert
        Assert.True(fighter.IsOnStation);
        Assert.NotNull(fighter.OnStationTime);
    }

    [Fact]
    public void Aircraft_CapPatrol_EnforcesSectorBoundary()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        
        // Position at edge of sector (90% of radius)
        Vec2 edgePosition = sectorCenter + new Vec2(sectorRadius * 0.9, 0);
        fighter.Position = edgePosition;
        fighter.HeadingDeg = 90; // Flying away from center
        fighter.SyncPhysicsState();

        // Act
        fighter.Update(1.0);

        // Assert - Should turn toward sector center
        double headingToCenter = edgePosition.HeadingTo(sectorCenter);
        Assert.Equal(headingToCenter, fighter.RequestedHeadingDeg, 5.0); // Within 5 degrees
    }

    [Fact]
    public void Aircraft_CapPatrol_MaintainsStandardAltitude()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        fighter.Position = sectorCenter;
        fighter.SyncPhysicsState();

        // Act
        fighter.Update(1.0);

        // Assert - Should request standard CAP altitude (25,000 ft)
        double expectedAltitudeM = CoordinateSystem.FtToM(25000);
        Assert.Equal(expectedAltitudeM, fighter.RequestedAltitudeM, 100.0); // Within 100m
    }

    [Fact]
    public void Aircraft_IsPositionInSector_ReturnsTrueForPositionWithinRadius()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;

        Vec2 positionInSector = sectorCenter + new Vec2(5000, 5000); // Well within radius

        // Act
        bool result = fighter.IsPositionInSector(positionInSector);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Aircraft_IsPositionInSector_ReturnsFalseForPositionOutsideRadius()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;

        Vec2 positionOutsideSector = sectorCenter + new Vec2(sectorRadius + 10000, 0); // Outside radius

        // Act
        bool result = fighter.IsPositionInSector(positionOutsideSector);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Aircraft_SetInterceptCourse_CalculatesHeadingToTarget()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Position = new Vec2(0, 0);
        fighter.SyncPhysicsState();

        Vec2 targetPosition = new Vec2(50000, 50000);
        Vec2 targetVelocity = new Vec2(100, 0); // Moving east

        // Act
        fighter.SetInterceptCourse(targetPosition, targetVelocity);

        // Assert - Should set heading toward target area
        Assert.True(fighter.RequestedHeadingDeg >= 0 && fighter.RequestedHeadingDeg < 360);
        Assert.Equal(fighter.FlightModel.MaxSpeedMps, fighter.RequestedSpeedMps); // Full speed
    }

    [Fact]
    public void Aircraft_SetInterceptCourse_SetsFullSpeed()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Position = new Vec2(0, 0);
        fighter.SyncPhysicsState();

        Vec2 targetPosition = new Vec2(50000, 50000);
        Vec2 targetVelocity = new Vec2(100, 0);

        // Act
        fighter.SetInterceptCourse(targetPosition, targetVelocity);

        // Assert
        Assert.Equal(fighter.FlightModel.MaxSpeedMps, fighter.RequestedSpeedMps);
    }

    [Fact]
    public void Aircraft_DetectHostileInSector_ReturnsTrueForHostileInRange()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.Position = sectorCenter;
        fighter.SyncPhysicsState();

        // Create hostile within sector and detection range
        var hostile = Aircraft.CreateFromType("MIG-29", Affiliation.Hostile);
        hostile.Position = sectorCenter + new Vec2(10000, 0); // 10km from center
        hostile.SyncPhysicsState();

        var hostiles = new List<Entity> { hostile };

        // Act
        bool detected = fighter.DetectHostileInSector(hostiles, out Entity? detectedTarget);

        // Assert
        Assert.True(detected);
        Assert.NotNull(detectedTarget);
        Assert.Equal(hostile.Id, detectedTarget.Id);
    }

    [Fact]
    public void Aircraft_DetectHostileInSector_ReturnsFalseForHostileOutsideSector()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.Position = sectorCenter;
        fighter.SyncPhysicsState();

        // Create hostile outside sector
        var hostile = Aircraft.CreateFromType("MIG-29", Affiliation.Hostile);
        hostile.Position = sectorCenter + new Vec2(sectorRadius + 20000, 0); // Outside sector
        hostile.SyncPhysicsState();

        var hostiles = new List<Entity> { hostile };

        // Act
        bool detected = fighter.DetectHostileInSector(hostiles, out Entity? detectedTarget);

        // Assert
        Assert.False(detected);
        Assert.Null(detectedTarget);
    }

    [Fact]
    public void Aircraft_DetectHostileInSector_ReturnsFalseForHostileBeyondDetectionRange()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(50); // Large sector
        
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.Position = sectorCenter;
        fighter.SyncPhysicsState();

        // Create hostile in sector but beyond detection range (>40nm)
        var hostile = Aircraft.CreateFromType("MIG-29", Affiliation.Hostile);
        hostile.Position = sectorCenter + new Vec2(CoordinateSystem.NmToMeters(45), 0); // 45nm away
        hostile.SyncPhysicsState();

        var hostiles = new List<Entity> { hostile };

        // Act
        bool detected = fighter.DetectHostileInSector(hostiles, out Entity? detectedTarget);

        // Assert
        Assert.False(detected);
        Assert.Null(detectedTarget);
    }

    [Fact]
    public void Aircraft_DetectHostileInSector_IgnoresInactiveEntities()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.Position = sectorCenter;
        fighter.SyncPhysicsState();

        // Create inactive hostile within sector
        var hostile = Aircraft.CreateFromType("MIG-29", Affiliation.Hostile);
        hostile.Position = sectorCenter + new Vec2(10000, 0);
        hostile.Status = EntityStatus.Destroyed; // Inactive
        hostile.SyncPhysicsState();

        var hostiles = new List<Entity> { hostile };

        // Act
        bool detected = fighter.DetectHostileInSector(hostiles, out Entity? detectedTarget);

        // Assert
        Assert.False(detected);
        Assert.Null(detectedTarget);
    }

    // ── Weapon Engagement Logic Tests (Task 1.3) ──────────────────────

    [Fact]
    public void Aircraft_SelectWeaponForTarget_BvrRange_SelectsAim120()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;
        double rangeNm = 20.0; // BVR range

        // Act
        var (weapon, canEngage) = fighter.SelectWeaponForTarget(rangeNm);

        // Assert
        Assert.Equal(WeaponType.Aim120, weapon);
        Assert.True(canEngage);
    }

    [Fact]
    public void Aircraft_SelectWeaponForTarget_WvrRange_SelectsAim9()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;
        double rangeNm = 8.0; // WVR range

        // Act
        var (weapon, canEngage) = fighter.SelectWeaponForTarget(rangeNm);

        // Assert
        Assert.Equal(WeaponType.Aim9, weapon);
        Assert.True(canEngage);
    }

    [Fact]
    public void Aircraft_SelectWeaponForTarget_WvrRangeNoAim9_FallsBackToAim120()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 0; // No AIM-9
        double rangeNm = 8.0; // WVR range

        // Act
        var (weapon, canEngage) = fighter.SelectWeaponForTarget(rangeNm);

        // Assert
        Assert.Equal(WeaponType.Aim120, weapon);
        Assert.True(canEngage);
    }

    [Fact]
    public void Aircraft_SelectWeaponForTarget_Winchester_CannotEngage()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 0;
        fighter.Aim9Count = 0;
        double rangeNm = 15.0;

        // Act
        var (weapon, canEngage) = fighter.SelectWeaponForTarget(rangeNm);

        // Assert
        Assert.Equal(WeaponType.None, weapon);
        Assert.False(canEngage);
    }

    [Fact]
    public void Aircraft_SelectWeaponForTarget_OutOfRange_CannotEngage()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;
        double rangeNm = 50.0; // Beyond AIM-120 max range

        // Act
        var (weapon, canEngage) = fighter.SelectWeaponForTarget(rangeNm);

        // Assert
        Assert.Equal(WeaponType.None, weapon);
        Assert.False(canEngage);
    }

    [Fact]
    public void Aircraft_SelectWeaponForTarget_TooClose_CannotEngage()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;
        double rangeNm = 0.2; // Below AIM-9 min range

        // Act
        var (weapon, canEngage) = fighter.SelectWeaponForTarget(rangeNm);

        // Assert
        Assert.Equal(WeaponType.None, weapon);
        Assert.False(canEngage);
    }

    [Fact]
    public void Aircraft_IsInAim120Range_WithinRange_ReturnsTrue()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        double rangeNm = 20.0; // Within AIM-120 range (3-30nm)

        // Act
        bool inRange = fighter.IsInAim120Range(rangeNm);

        // Assert
        Assert.True(inRange);
    }

    [Fact]
    public void Aircraft_IsInAim120Range_TooFar_ReturnsFalse()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        double rangeNm = 35.0; // Beyond AIM-120 max range

        // Act
        bool inRange = fighter.IsInAim120Range(rangeNm);

        // Assert
        Assert.False(inRange);
    }

    [Fact]
    public void Aircraft_IsInAim120Range_TooClose_ReturnsFalse()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        double rangeNm = 2.0; // Below AIM-120 min range

        // Act
        bool inRange = fighter.IsInAim120Range(rangeNm);

        // Assert
        Assert.False(inRange);
    }

    [Fact]
    public void Aircraft_IsInAim9Range_WithinRange_ReturnsTrue()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        double rangeNm = 5.0; // Within AIM-9 range (0.5-10nm)

        // Act
        bool inRange = fighter.IsInAim9Range(rangeNm);

        // Assert
        Assert.True(inRange);
    }

    [Fact]
    public void Aircraft_IsInAim9Range_TooFar_ReturnsFalse()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        double rangeNm = 12.0; // Beyond AIM-9 max range

        // Act
        bool inRange = fighter.IsInAim9Range(rangeNm);

        // Assert
        Assert.False(inRange);
    }

    [Fact]
    public void Aircraft_IsInAim9Range_TooClose_ReturnsFalse()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        double rangeNm = 0.3; // Below AIM-9 min range

        // Act
        bool inRange = fighter.IsInAim9Range(rangeNm);

        // Assert
        Assert.False(inRange);
    }

    [Fact]
    public void Aircraft_IsTargetInWeaponRange_Aim120Range_ReturnsTrue()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 0;
        double rangeNm = 20.0; // AIM-120 range

        // Act
        bool inRange = fighter.IsTargetInWeaponRange(rangeNm);

        // Assert
        Assert.True(inRange);
    }

    [Fact]
    public void Aircraft_IsTargetInWeaponRange_Aim9Range_ReturnsTrue()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 0;
        fighter.Aim9Count = 2;
        double rangeNm = 5.0; // AIM-9 range

        // Act
        bool inRange = fighter.IsTargetInWeaponRange(rangeNm);

        // Assert
        Assert.True(inRange);
    }

    [Fact]
    public void Aircraft_IsTargetInWeaponRange_NoWeapons_ReturnsFalse()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 0;
        fighter.Aim9Count = 0;
        double rangeNm = 15.0;

        // Act
        bool inRange = fighter.IsTargetInWeaponRange(rangeNm);

        // Assert
        Assert.False(inRange);
    }

    [Fact]
    public void Aircraft_IsTargetInWeaponRange_OutOfAllRanges_ReturnsFalse()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;
        double rangeNm = 50.0; // Beyond all weapon ranges

        // Act
        bool inRange = fighter.IsTargetInWeaponRange(rangeNm);

        // Assert
        Assert.False(inRange);
    }

    [Fact]
    public void Aircraft_ExpendWeapon_Aim120_DecreasesCount()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;

        // Act
        bool expended = fighter.ExpendWeapon(WeaponType.Aim120);

        // Assert
        Assert.True(expended);
        Assert.Equal(3, fighter.Aim120Count);
        Assert.Equal(2, fighter.Aim9Count); // AIM-9 unchanged
    }

    [Fact]
    public void Aircraft_ExpendWeapon_Aim9_DecreasesCount()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;

        // Act
        bool expended = fighter.ExpendWeapon(WeaponType.Aim9);

        // Assert
        Assert.True(expended);
        Assert.Equal(4, fighter.Aim120Count); // AIM-120 unchanged
        Assert.Equal(1, fighter.Aim9Count);
    }

    [Fact]
    public void Aircraft_ExpendWeapon_NoAim120_ReturnsFalse()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 0;
        fighter.Aim9Count = 2;

        // Act
        bool expended = fighter.ExpendWeapon(WeaponType.Aim120);

        // Assert
        Assert.False(expended);
        Assert.Equal(0, fighter.Aim120Count);
    }

    [Fact]
    public void Aircraft_ExpendWeapon_NoAim9_ReturnsFalse()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 0;

        // Act
        bool expended = fighter.ExpendWeapon(WeaponType.Aim9);

        // Assert
        Assert.False(expended);
        Assert.Equal(0, fighter.Aim9Count);
    }

    [Fact]
    public void Aircraft_ExpendWeapon_MultipleExpenditure_TracksCorrectly()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;

        // Act
        fighter.ExpendWeapon(WeaponType.Aim120);
        fighter.ExpendWeapon(WeaponType.Aim120);
        fighter.ExpendWeapon(WeaponType.Aim9);

        // Assert
        Assert.Equal(2, fighter.Aim120Count);
        Assert.Equal(1, fighter.Aim9Count);
        Assert.False(fighter.IsWinchester);
    }

    [Fact]
    public void Aircraft_ExpendWeapon_AllWeapons_BecomesWinchester()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 1;
        fighter.Aim9Count = 1;

        // Act
        fighter.ExpendWeapon(WeaponType.Aim120);
        fighter.ExpendWeapon(WeaponType.Aim9);

        // Assert
        Assert.Equal(0, fighter.Aim120Count);
        Assert.Equal(0, fighter.Aim9Count);
        Assert.True(fighter.IsWinchester);
    }

    [Fact]
    public void Aircraft_ShouldRtbDueToWinchester_Winchester_ReturnsTrue()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 0;
        fighter.Aim9Count = 0;
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;

        // Act
        bool shouldRtb = fighter.ShouldRtbDueToWinchester();

        // Assert
        Assert.True(shouldRtb);
    }

    [Fact]
    public void Aircraft_ShouldRtbDueToWinchester_HasWeapons_ReturnsFalse()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 2;
        fighter.Aim9Count = 1;
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;

        // Act
        bool shouldRtb = fighter.ShouldRtbDueToWinchester();

        // Assert
        Assert.False(shouldRtb);
    }

    [Fact]
    public void Aircraft_ShouldRtbDueToWinchester_AlreadyEgressing_ReturnsFalse()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 0;
        fighter.Aim9Count = 0;
        fighter.CurrentBehavior = AircraftBehavior.EgressRetreat;

        // Act
        bool shouldRtb = fighter.ShouldRtbDueToWinchester();

        // Assert
        Assert.False(shouldRtb); // Already RTB, no need to trigger again
    }

    [Fact]
    public void Aircraft_WeaponRanges_BoundaryConditions()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;

        // Act & Assert - AIM-120 boundaries
        Assert.True(fighter.IsInAim120Range(3.0));   // Min range
        Assert.True(fighter.IsInAim120Range(30.0));  // Max range
        Assert.False(fighter.IsInAim120Range(2.9));  // Just below min
        Assert.False(fighter.IsInAim120Range(30.1)); // Just above max

        // Act & Assert - AIM-9 boundaries
        Assert.True(fighter.IsInAim9Range(0.5));   // Min range
        Assert.True(fighter.IsInAim9Range(10.0));  // Max range
        Assert.False(fighter.IsInAim9Range(0.4));  // Just below min
        Assert.False(fighter.IsInAim9Range(10.1)); // Just above max
    }

    [Fact]
    public void Aircraft_SelectWeaponForTarget_BvrWvrTransition()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.Aim120Count = 4;
        fighter.Aim9Count = 2;

        // Act & Assert - Just above BVR threshold (12nm) - should use AIM-120
        var (weaponBvr, canEngageBvr) = fighter.SelectWeaponForTarget(12.1);
        Assert.Equal(WeaponType.Aim120, weaponBvr);
        Assert.True(canEngageBvr);

        // Act & Assert - Below BVR threshold but within AIM-9 range - should use AIM-9
        var (weaponWvr, canEngageWvr) = fighter.SelectWeaponForTarget(9.0);
        Assert.Equal(WeaponType.Aim9, weaponWvr);
        Assert.True(canEngageWvr);
        
        // Act & Assert - Below BVR threshold but outside AIM-9 range - should fallback to AIM-120
        var (weaponFallback, canEngageFallback) = fighter.SelectWeaponForTarget(11.0);
        Assert.Equal(WeaponType.Aim120, weaponFallback);
        Assert.True(canEngageFallback);
    }
}


/// <summary>
/// Tests for EntityManager.SpawnFriendlyFighter method.
/// Validates Task 1.4 from real-support-entities spec.
/// </summary>
public class EntityManagerFriendlyFighterSpawnTests
{
    [Fact]
    public void EntityManager_SpawnFriendlyFighter_CreatesF16WithCorrectAffiliation()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert
        Assert.NotNull(fighter);
        Assert.Equal("F-16C VIPER", fighter.Designation);
        Assert.Equal(Affiliation.Friendly, fighter.Affiliation);
        Assert.Equal(AircraftRole.Fighter, fighter.Role);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_SetsCallsign()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert
        Assert.Equal("VIPER-1", fighter.CallSign);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_AssignsPatrolSector()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert
        Assert.Equal("SECTOR-ALPHA", fighter.PatrolSectorId);
        Assert.Equal(sectorCenter, fighter.PatrolSectorCenter);
        Assert.Equal(CoordinateSystem.NmToMeters(sectorRadiusNm), fighter.PatrolSectorRadiusM);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_PositionsAtSectorEntryPoint()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;
        double sectorRadiusM = CoordinateSystem.NmToMeters(sectorRadiusNm);

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert - Should be positioned at sector radius distance from center
        double distanceToCenter = fighter.Position.DistanceTo(sectorCenter);
        Assert.Equal(sectorRadiusM, distanceToCenter, 1.0); // Within 1 meter tolerance
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_SetsHeadingTowardSectorCenter()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert - Heading should point toward sector center
        double expectedHeading = fighter.Position.HeadingTo(sectorCenter);
        Assert.Equal(expectedHeading, fighter.HeadingDeg, 1.0); // Within 1 degree
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_InitializesAltitudeAndSpeed()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert - Standard CAP altitude and cruise speed
        double expectedAltitudeM = CoordinateSystem.FtToM(25000);
        double expectedSpeedMps = fighter.FlightModel.MaxSpeedMps * 0.75;
        
        Assert.Equal(expectedAltitudeM, fighter.AltitudeM, 1.0);
        Assert.Equal(expectedSpeedMps, fighter.SpeedMps, 1.0);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_InitializesFullFuel()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert
        Assert.Equal(fighter.FuelCapacityKg, fighter.FuelRemainingKg);
        Assert.False(fighter.IsOnRTB);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_InitializesDefaultWeaponLoadout()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert - Default loadout: 4x AIM-120, 2x AIM-9
        Assert.Equal(4, fighter.Aim120Count);
        Assert.Equal(2, fighter.Aim9Count);
        Assert.False(fighter.IsWinchester);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_AcceptsCustomWeaponLoadout()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm,
            aim120Count: 6, aim9Count: 4);

        // Assert - Custom loadout
        Assert.Equal(6, fighter.Aim120Count);
        Assert.Equal(4, fighter.Aim9Count);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_SetsBehaviorToOrbitPatrol()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert
        Assert.Equal(AircraftBehavior.OrbitPatrol, fighter.CurrentBehavior);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_RegistersWithEntityManager()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert - Entity should be registered and retrievable
        var retrieved = entityManager.Get(fighter.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(fighter.Id, retrieved.Id);
        Assert.Equal(fighter.CallSign, retrieved.CallSign);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_SetsSpawnTime()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;
        var beforeSpawn = DateTime.UtcNow;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert
        var afterSpawn = DateTime.UtcNow;
        Assert.True(fighter.SpawnTime >= beforeSpawn);
        Assert.True(fighter.SpawnTime <= afterSpawn);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_SyncsPhysicsState()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert - Physics state should be synced (velocity components should be set)
        double velocityMagnitude = Math.Sqrt(fighter.VelocityX * fighter.VelocityX + 
                                             fighter.VelocityY * fighter.VelocityY);
        Assert.True(velocityMagnitude > 0);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_MultipleSpawns_AllRegistered()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter1 = new Vec2(50000, 30000);
        var sectorCenter2 = new Vec2(60000, 40000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter1 = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter1, sectorRadiusNm);
        var fighter2 = entityManager.SpawnFriendlyFighter(
            "VIPER-2", "SECTOR-BRAVO", sectorCenter2, sectorRadiusNm);

        // Assert
        Assert.NotEqual(fighter1.Id, fighter2.Id);
        Assert.Equal(2, entityManager.Count);
        
        var retrieved1 = entityManager.Get(fighter1.Id);
        var retrieved2 = entityManager.Get(fighter2.Id);
        
        Assert.NotNull(retrieved1);
        Assert.NotNull(retrieved2);
        Assert.Equal("VIPER-1", retrieved1.CallSign);
        Assert.Equal("VIPER-2", retrieved2.CallSign);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_IsActiveAfterSpawn()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        // Act
        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        // Assert
        Assert.True(fighter.IsActive);
        Assert.Equal(EntityStatus.Active, fighter.Status);
    }

    [Fact]
    public void EntityManager_SpawnFriendlyFighter_CanUpdateAfterSpawn()
    {
        // Arrange
        var entityManager = new EntityManager();
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadiusNm = 20.0;

        var fighter = entityManager.SpawnFriendlyFighter(
            "VIPER-1", "SECTOR-ALPHA", sectorCenter, sectorRadiusNm);

        double initialFuel = fighter.FuelRemainingKg;

        // Act - Update should work without errors
        fighter.Update(1.0);

        // Assert - Fuel should have decreased
        Assert.True(fighter.FuelRemainingKg < initialFuel);
    }
}
