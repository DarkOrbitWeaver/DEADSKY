using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;
using System.Threading;

namespace DEADSKY.Backend.Tests;

public class TrackManagerTests
{
    [Fact]
    public void ProcessDetection_CarriesAircraftGroupIdentity_IntoTrack()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var aircraft = sim.Entities.SpawnAircraftAtBearingRange(
            "MiG-29",
            Affiliation.Hostile,
            35,
            40,
            18000,
            220,
            450);
        aircraft.GroupId = "LANCER-1";

        var track = sim.Radar.TrackManager.ProcessDetection(aircraft, 35, iffResponse: false, radarNoise: 0);

        Assert.Equal("LANCER-1", track.GroupLabel);
        Assert.Contains("MiG-29", track.TrackDesignation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessDetection_HostileWithoutIff_BecomesAssumedHostile()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var aircraft = sim.Entities.SpawnAircraftAtBearingRange(
            "MiG-29",
            Affiliation.Hostile,
            40,
            38,
            18000,
            220,
            450);

        var track = sim.Radar.TrackManager.ProcessDetection(aircraft, 40, iffResponse: false, radarNoise: 0);

        Assert.Equal(DEADSKY.Core.Radar.TrackClassification.AssumedHostile, track.Classification);
        Assert.False(track.IFFResponse);
        Assert.True(track.IFFInterrogated);
    }

    [Fact]
    public void ProcessDetection_RepeatedHostileDetections_PromoteToHostile()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var aircraft = sim.Entities.SpawnAircraftAtBearingRange(
            "MiG-29",
            Affiliation.Hostile,
            45,
            34,
            18000,
            220,
            450);

        var first = sim.Radar.TrackManager.ProcessDetection(aircraft, 45, iffResponse: false, radarNoise: 0);
        var second = sim.Radar.TrackManager.ProcessDetection(aircraft, 45, iffResponse: false, radarNoise: 0);
        var third = sim.Radar.TrackManager.ProcessDetection(aircraft, 45, iffResponse: false, radarNoise: 0);

        Assert.Equal(first.TrackId, second.TrackId);
        Assert.Equal(first.TrackId, third.TrackId);
        Assert.Equal(DEADSKY.Core.Radar.TrackClassification.Hostile, third.Classification);
        Assert.True(third.ClassificationConfidence >= 0.95);
    }

    [Fact]
    public void Update_DoesNotCoastTrack_OnSameTickAsDetection()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var aircraft = sim.Entities.SpawnAircraftAtBearingRange(
            "MiG-29",
            Affiliation.Hostile,
            45,
            30,
            18000,
            225,
            450);

        var track = sim.Radar.TrackManager.ProcessDetection(aircraft, 45, iffResponse: false, radarNoise: 0);
        var originalPosition = track.Position;

        sim.Radar.TrackManager.Update(1.0);

        Assert.Equal(originalPosition.X, track.Position.X, 6);
        Assert.Equal(originalPosition.Y, track.Position.Y, 6);
    }

    [Fact]
    public void UpdateWithDetection_UsesStableKinematics_ForDesignatedTrack()
    {
        SimulationRandom.Seed(17);
        var track = new DEADSKY.Core.Radar.TrackFile();

        track.UpdateWithDetection(new Vec2(0, 0), 4500, 90, 220, 300);
        track.IsDesignated = true;
        Thread.Sleep(120);
        track.UpdateWithDetection(new Vec2(220, 0), 4500, 90, 220, 300);

        Assert.InRange(track.SpeedMps, 215, 225);
        Assert.InRange(track.PositionUncertaintyM, 75, 100);
        Assert.True(track.Position.X > -50);
    }
}
