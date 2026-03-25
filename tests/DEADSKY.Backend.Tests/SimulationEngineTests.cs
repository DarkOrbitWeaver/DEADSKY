using DEADSKY.Core.Entities;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;
using System.Linq;

namespace DEADSKY.Backend.Tests;

public class SimulationEngineTests
{
    [Fact]
    public void LoadScenario_AppliesBatteryConfig_AndQueuesOpeningMessage()
    {
        using var sim = new SimulationEngine();
        var scenario = SimulationTestFactory.CreateSingleBogeyScenario();
        var manager = new ScenarioManager(sim);

        manager.LoadScenario(scenario);

        var battery = sim.Entities.GetPlayerBattery();

        Assert.NotNull(battery);
        Assert.Equal(12, battery!.ReserveMissiles);
        Assert.Equal(80, battery.RadarRangeNm);
        Assert.Equal(RulesOfEngagement.WeaponsTight, battery.ROE);
        Assert.Equal(BatteryAlertLevel.Yellow, battery.AlertLevel);

        var opening = SimulationTestFactory.DrainSingleQueuedMessage(sim.Comms);
        Assert.Equal("ECHO ACTUAL", opening.SenderCallsign);
        Assert.Contains("SINGLE BOGEY", opening.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayerFire_OnDetectedHostileTrack_LaunchesMissileAndConsumesLauncher()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var battery = sim.Entities.GetPlayerBattery()!;
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim);

        var fired = sim.PlayerFire(track.TrackId);

        Assert.True(fired, sim.Weapons.LastError);
        Assert.Single(sim.Entities.GetActiveMissiles());
        Assert.Equal(3, battery.ReadyLaunchers);
        Assert.Equal(1, battery.MissilesFired);
        Assert.Equal(RadarMode.SingleTargetTrack, battery.RadarMode);
    }

    [Fact]
    public void PlayerFire_RejectsTrackOutsideEnvelope_AndPreservesLauncherState()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var battery = sim.Entities.GetPlayerBattery()!;
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, rangeNm: 40);

        var fired = sim.PlayerFire(track.TrackId);

        Assert.False(fired);
        Assert.Equal(4, battery.ReadyLaunchers);
        Assert.Equal(0, battery.MissilesFired);
        Assert.Contains("out of range", sim.Weapons.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayerSetRadarControls_RefreshLatestSnapshotImmediately()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();

        sim.PlayerSetRadarMode(RadarMode.Standby);
        sim.PlayerSetRadarRange(40);

        Assert.Equal(RadarMode.Standby, sim.LatestSnapshot.RadarMode);
        Assert.Equal(40, sim.LatestSnapshot.RadarRangeNm);
        Assert.False(sim.Entities.GetPlayerBattery()!.RadarOnline);
    }

    [Theory]
    [InlineData(5, 40)]
    [InlineData(63, 80)]
    [InlineData(220, 120)]
    public void PlayerSetRadarRange_NormalizesToSupportedPresets(double requestedRange, double expectedRange)
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();

        sim.PlayerSetRadarRange(requestedRange);

        Assert.Equal(expectedRange, sim.LatestSnapshot.RadarRangeNm);
        Assert.Equal(expectedRange, sim.Entities.GetPlayerBattery()!.RadarRangeNm);
    }

    [Fact]
    public void FlushPendingComms_DeliversQueuedOpeningMessage_WithoutSimulationTick()
    {
        using var sim = new SimulationEngine();
        var manager = new ScenarioManager(sim);
        manager.LoadScenario(SimulationTestFactory.CreateSingleBogeyScenario());

        var delivered = sim.FlushPendingComms();
        var history = sim.Comms.GetHistory(RadioChannel.CommandNet);

        Assert.True(delivered > 0);
        Assert.Contains(history, message => message.Content.Contains("SINGLE BOGEY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SemiActiveMissile_LosesGuidance_WhenRadarStopsIlluminating()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, rangeNm: 12);

        var fired = sim.PlayerFire(track.TrackId);

        Assert.True(fired, sim.Weapons.LastError);
        var missile = sim.Entities.GetActiveMissiles().Single();

        sim.PlayerSetRadarMode(RadarMode.Silent);
        sim.Weapons.Update(0.1);

        Assert.False(missile.GuidanceActive);
        Assert.Equal(GuidanceMode.SemiActiveRadar, missile.Guidance);
    }

    [Fact]
    public void GuidedMissile_ClosesOnTarget_AfterLaunch()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var track = SimulationTestFactory.AddDetectedHostileTrack(
            sim,
            bearingDeg: 45,
            rangeNm: 14,
            altitudeFt: 18000,
            headingDeg: 225,
            speedKts: 420);

        var fired = sim.PlayerFire(track.TrackId);

        Assert.True(fired, sim.Weapons.LastError);

        var missile = sim.Entities.GetActiveMissiles().Single();
        var target = sim.Entities.Get(track.EntityId!)!;
        double initialSeparation = missile.Position.DistanceTo(target.Position);

        for (int i = 0; i < 10; i++)
        {
            sim.Weapons.Update(0.1);
            sim.Entities.UpdateAll(0.1);
        }

        double updatedSeparation = missile.Position.DistanceTo(target.Position);

        Assert.True(updatedSeparation < initialSeparation,
            $"Missile failed to close distance. Initial {initialSeparation:F1}m, updated {updatedSeparation:F1}m.");
        Assert.True(Math.Abs(FlightModel.NormalizeHeadingDiff(
                missile.Position.HeadingTo(target.Position) - missile.HeadingDeg)) < 90,
            $"Missile heading diverged from target. Missile heading {missile.HeadingDeg:F1}, target bearing {missile.Position.HeadingTo(target.Position):F1}.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(180)]
    [InlineData(225)]
    [InlineData(270)]
    [InlineData(315)]
    public void GuidedMissile_FirstTick_MovesTowardTargetAcrossQuadrants(double bearingDeg)
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var track = SimulationTestFactory.AddDetectedHostileTrack(
            sim,
            bearingDeg: bearingDeg,
            rangeNm: 16,
            altitudeFt: 18000,
            headingDeg: (bearingDeg + 180) % 360,
            speedKts: 420);

        var fired = sim.PlayerFire(track.TrackId);

        Assert.True(fired, sim.Weapons.LastError);

        var missile = sim.Entities.GetActiveMissiles().Single();
        var target = sim.Entities.Get(track.EntityId!)!;
        Vec2 launchPosition = missile.Position;
        Vec2 toTargetAtLaunch = target.Position - launchPosition;

        sim.Weapons.Update(0.1);
        sim.Entities.UpdateAll(0.1);

        Vec2 displacement = missile.Position - launchPosition;
        Assert.True(displacement.Dot(toTargetAtLaunch) > 0,
            $"Missile moved away from target on first tick. Bearing {bearingDeg:F0}, displacement {displacement}, target vector {toTargetAtLaunch}.");
    }

    [Fact]
    public void PredictIntercept_LeadsCrossingTarget_InFrontOfCurrentPosition()
    {
        Vec2 launchPos = Vec2.Zero;
        Vec2 targetPos = new(0, 10_000);
        Vec2 targetVelocity = new(250, 0);

        var intercept = MissileKinematics.PredictIntercept(
            launchPos,
            missileSpeedMps: 900,
            targetPos,
            targetVelocity,
            maxFlightTimeSec: 60);

        Assert.NotNull(intercept);
        Assert.True(intercept!.Value.X > targetPos.X,
            $"Intercept point should lead the crossing target. Current X {targetPos.X:F1}, intercept X {intercept.Value.X:F1}.");
        Assert.True(intercept.Value.Y > 0,
            $"Intercept point should remain ahead of the launcher. Intercept Y {intercept.Value.Y:F1}.");
    }

    [Fact]
    public void DestroyedEntity_IsNotRemovedImmediately_BecauseOfOldSpawnTime()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var aircraft = sim.Entities.SpawnAircraftAtBearingRange(
            "MiG-29",
            Affiliation.Hostile,
            30,
            20,
            18000,
            220,
            450);

        aircraft.SpawnTime = DateTime.UtcNow.AddMinutes(-5);
        aircraft.Status = EntityStatus.Destroyed;

        sim.Entities.UpdateAll(0.1);

        Assert.NotNull(sim.Entities.Get(aircraft.Id));
    }

    [Fact]
    public void MissileExpiry_ClearsTrackEngagementFlags()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, rangeNm: 15, speedKts: 320);

        var fired = sim.PlayerFire(track.TrackId);

        Assert.True(fired, sim.Weapons.LastError);
        Assert.True(track.IsBeingEngaged);
        Assert.False(string.IsNullOrWhiteSpace(track.AssignedMissileId));

        var target = sim.Entities.Get(track.EntityId!)!;
        target.Position = CoordinateSystem.FromBearingRange(45, 160);
        target.SyncPhysicsState();

        for (int i = 0; i < 320; i++)
        {
            sim.Entities.UpdateAll(0.1);
            sim.Weapons.Update(0.1);
        }

        var survivingTrack = sim.Radar.TrackManager.GetById(track.TrackId);
        Assert.NotNull(survivingTrack);
        Assert.False(survivingTrack!.IsBeingEngaged);
        Assert.Null(survivingTrack.AssignedMissileId);
    }
}
