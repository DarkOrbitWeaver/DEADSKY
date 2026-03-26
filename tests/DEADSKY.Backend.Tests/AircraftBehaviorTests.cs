using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;

namespace DEADSKY.Backend.Tests;

public class AircraftBehaviorTests
{
    [Fact]
    public void Update_FighterUnderHardLock_TransitionsToEscortCover_AndDispensesChaff()
    {
        var aircraft = Aircraft.CreateFromType("F-16", Affiliation.Hostile);
        aircraft.Position = CoordinateSystem.FromBearingRange(40, 28);
        aircraft.SyncPhysicsState();
        aircraft.RadarLockDetected = true;
        aircraft.RadarLockDetectedTime = DateTime.UtcNow;
        aircraft.HardLockDetected = true;
        aircraft.HardLockDetectedTime = DateTime.UtcNow;
        int initialChaff = aircraft.ChaffCharges;

        aircraft.Update(0.1);

        Assert.Equal(AircraftBehavior.EscortCover, aircraft.CurrentBehavior);
        Assert.True(aircraft.ChaffCharges < initialChaff);
        Assert.True(aircraft.RequestedSpeedMps > 0);
    }

    [Fact]
    public void Update_SeadAircraftUnderLock_TransitionsToSeadCommit()
    {
        var aircraft = new Aircraft
        {
            Affiliation = Affiliation.Hostile,
            Role = AircraftRole.SEAD,
            HasARMCapability = true,
            FlightModel = FlightModel.Fighter
        };
        aircraft.Position = CoordinateSystem.FromBearingRange(55, 34);
        aircraft.SyncPhysicsState();
        aircraft.RadarLockDetected = true;
        aircraft.RadarLockDetectedTime = DateTime.UtcNow;
        aircraft.HardLockDetected = true;
        aircraft.HardLockDetectedTime = DateTime.UtcNow;

        aircraft.Update(0.1);

        Assert.Equal(AircraftBehavior.SEAD, aircraft.CurrentBehavior);
        Assert.True(aircraft.RequestedAltitudeM > 0);
    }

    [Fact]
    public void Update_MissileInbound_TransitionsToEvasive_AndDispensesFlares()
    {
        var aircraft = Aircraft.CreateFromType("SU-24", Affiliation.Hostile);
        aircraft.Position = CoordinateSystem.FromBearingRange(25, 18);
        aircraft.SyncPhysicsState();
        aircraft.MissileInbound = true;
        aircraft.MissileInboundDetectedTime = DateTime.UtcNow;
        int initialFlares = aircraft.FlareCharges;

        aircraft.Update(0.1);

        Assert.Equal(AircraftBehavior.EvasiveManeuver, aircraft.CurrentBehavior);
        Assert.True(aircraft.FlareCharges < initialFlares);
        Assert.True(aircraft.RequestedSpeedMps >= aircraft.FlightModel.MaxSpeedMps * 0.95);
    }
}
