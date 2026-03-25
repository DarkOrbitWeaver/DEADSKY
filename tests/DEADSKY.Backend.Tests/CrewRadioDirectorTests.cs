using DEADSKY.Core.Personnel;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Backend.Tests;

public class CrewRadioDirectorTests
{
    [Fact]
    public void SelectResponder_PicksRadarOperator_ForPictureRequests()
    {
        var roster = CrewRoster.CreateDefaultCrew();

        var responder = CrewRadioDirector.SelectResponder(roster, "Give me a picture on the eastern raid.");

        Assert.Equal(SoldierRole.RadarOperator, responder.Role);
    }

    [Fact]
    public void BuildFallbackLine_UsesSnapshotContext()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        SimulationTestFactory.AddDetectedHostileTrack(sim);
        var roster = CrewRoster.CreateDefaultCrew();
        var responder = CrewRadioDirector.SelectResponder(roster, "Fire when ready.");
        sim.PlayerSetRadarMode(DEADSKY.Core.Entities.RadarMode.TrackWhileScan);

        var snapshot = new SimulationSnapshot
        {
            Battery = sim.Entities.GetPlayerBattery(),
            HostileTracks = sim.Radar.TrackManager.GetHostileTracks().ToList(),
            AllTracks = sim.Radar.TrackManager.GetAllTracks().ToList(),
            GameTimeSec = 15,
            GameTimeString = "00:00:15 ZULU",
            ActiveMissiles = sim.Entities.GetActiveMissiles().ToList(),
            RadarMode = DEADSKY.Core.Entities.RadarMode.TrackWhileScan,
            RadarRangeNm = 80,
            Weather = sim.Weather.Clone()
        };

        var line = CrewRadioDirector.BuildFallbackLine(responder, "Fire when ready", snapshot);

        Assert.Contains("launcher", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("awaiting commit", line, StringComparison.OrdinalIgnoreCase);
    }
}
