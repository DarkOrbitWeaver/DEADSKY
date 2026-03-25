using DEADSKY.Core.Campaign;
using DEADSKY.Core.Scenario;

namespace DEADSKY.Backend.Tests;

public class BattleIntelDirectorTests
{
    [Fact]
    public void Build_UsesSectorObjectivesAndLandmarks()
    {
        var scenario = SimulationTestFactory.CreateMultiWaveScenario();
        scenario.SectorMap = new SectorMapConfig
        {
            TheaterName = "Kovran Lowlands",
            Objectives = new List<MapObjectiveConfig>
            {
                new() { Id = "depot", Name = "Kovran Depot", BearingDeg = 128, RangeNm = 58, Importance = "primary" }
            },
            Landmarks = new List<MapLandmarkConfig>
            {
                new() { Name = "Sable Ridge", BearingDeg = 350, RangeNm = 56, Category = "ridge" }
            }
        };

        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        var summary = BattleIntelDirector.Build(scenario, sim.LatestSnapshot);

        Assert.Contains("KOVRAN", summary.SectorLore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEPOT", summary.ObjectiveBoard, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_ReportsRaidPackage_WhenHostileTrackExists()
    {
        var scenario = SimulationTestFactory.CreateSingleBogeyScenario();
        scenario.EnemyForces.Waves[0].PackageName = "RAVEN-1";

        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 45, rangeNm: 18);
        var snapshot = new DEADSKY.Core.Simulation.SimulationSnapshot
        {
            GameTimeSec = sim.LatestSnapshot.GameTimeSec,
            GameTimeString = sim.LatestSnapshot.GameTimeString,
            Battery = sim.LatestSnapshot.Battery,
            AllTracks = new[] { track },
            FirmTracks = new[] { track },
            HostileTracks = new[] { track },
            HostileAircraft = sim.LatestSnapshot.HostileAircraft,
            ActiveMissiles = sim.LatestSnapshot.ActiveMissiles,
            ActiveEcmEffects = sim.LatestSnapshot.ActiveEcmEffects,
            RadarSweepAngle = sim.LatestSnapshot.RadarSweepAngle,
            RadarRangeNm = sim.LatestSnapshot.RadarRangeNm,
            RadarMode = sim.LatestSnapshot.RadarMode,
            Weather = sim.LatestSnapshot.Weather
        };

        var summary = BattleIntelDirector.Build(scenario, snapshot);

        Assert.Contains("RAVEN-1", summary.PackageSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_MarksObjectiveUnderAttack_WhenHostileTrackApproachesObjective()
    {
        var scenario = SimulationTestFactory.CreateSingleBogeyScenario();
        scenario.SectorMap = new SectorMapConfig
        {
            TheaterName = "Kovran Lowlands",
            Objectives = new List<MapObjectiveConfig>
            {
                new() { Id = "target", Name = "North Field", BearingDeg = 45, RangeNm = 20, Importance = "primary" }
            }
        };
        scenario.EnemyForces.Waves[0].TargetObjectiveId = "target";

        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 45, rangeNm: 24);
        var snapshot = new DEADSKY.Core.Simulation.SimulationSnapshot
        {
            GameTimeSec = sim.LatestSnapshot.GameTimeSec,
            GameTimeString = sim.LatestSnapshot.GameTimeString,
            Battery = sim.LatestSnapshot.Battery,
            AllTracks = new[] { track },
            FirmTracks = new[] { track },
            HostileTracks = new[] { track },
            HostileAircraft = sim.LatestSnapshot.HostileAircraft,
            ActiveMissiles = sim.LatestSnapshot.ActiveMissiles,
            ActiveEcmEffects = sim.LatestSnapshot.ActiveEcmEffects,
            RadarSweepAngle = sim.LatestSnapshot.RadarSweepAngle,
            RadarRangeNm = sim.LatestSnapshot.RadarRangeNm,
            RadarMode = sim.LatestSnapshot.RadarMode,
            Weather = sim.LatestSnapshot.Weather
        };

        var summary = BattleIntelDirector.Build(scenario, snapshot);

        Assert.Contains("UNDER ATTACK", summary.ObjectiveBoard, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NORTH FIELD", summary.SectorEvent, StringComparison.OrdinalIgnoreCase);
    }
}
