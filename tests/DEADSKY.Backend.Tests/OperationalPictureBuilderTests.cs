using DEADSKY.Core.Campaign;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Backend.Tests;

public class OperationalPictureBuilderTests
{
    [Fact]
    public void Build_UsesScenarioObjectivesAndLandmarks_ForMarkersAndNotes()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        scenario.SectorMap.Landmarks.Add(new DEADSKY.Core.Scenario.MapLandmarkConfig
        {
            Name = "Sable Ridge",
            BearingDeg = 350,
            RangeNm = 56,
            Category = "ridge"
        });

        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        var picture = OperationalPictureBuilder.Build(sim.LatestSnapshot, scenario);

        Assert.Contains("KOVRAN LOWLANDS", picture.ScenarioHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sable Ridge", picture.ScenarioNotes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(picture.ObjectiveStates, objective => objective.Name.Contains("Kovran Depot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(picture.TacticalMarkers, marker => marker.MarkerId.StartsWith("objective-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(picture.TacticalMarkers, marker => marker.Kind == TacticalMarkerKind.Landmark);
    }

    [Fact]
    public void Build_SelectedTrackContext_AndCommsConsequences_AreGroundedInLiveState()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        var support = new FriendlySupportDirector(sim.Comms);
        support.InitializeForScenario(scenario, null);
        var hostile = SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 124, rangeNm: 54);
        sim.PlayerDesignate(hostile.TrackId);
        sim.Weapons.RecordOperationalIncident("check_fire_order", "Check-fire traffic tightened engagement discipline.", IncidentSeverity.Warning, hostile.TrackId, hostile.EntityId);
        sim.RefreshSnapshot();

        var picture = OperationalPictureBuilder.Build(
            sim.LatestSnapshot,
            scenario,
            sim.Weapons.Incidents,
            support.Packages,
            hostile.TrackId,
            0.62,
            "COMMAND POSTURE: INNER RING DEFENSE AROUND KOVRAN DEPOT.",
            "SUPPORT CONSEQUENCE: ACTIVE DEFENSIVE SHIFT TOWARD KOVRAN DEPOT.");

        Assert.NotNull(picture.SelectedTrack);
        Assert.Contains("Kovran Depot", picture.SelectedTrack!.DoctrineSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("check fire", picture.CommsConsequences.RoeSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KOVRAN DEPOT", picture.CommsConsequences.SupportImpactSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_IncludesObjectiveSupportAndJammingMarkers_ForSharedPictureParity()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        var support = new FriendlySupportDirector(sim.Comms);
        support.InitializeForScenario(scenario, null);
        support.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP now.", 0);
        support.Tick(80, 80);

        var snapshot = new SimulationSnapshot
        {
            GameTimeSec = sim.LatestSnapshot.GameTimeSec,
            GameTimeString = sim.LatestSnapshot.GameTimeString,
            Battery = sim.LatestSnapshot.Battery,
            AllTracks = sim.LatestSnapshot.AllTracks,
            FirmTracks = sim.LatestSnapshot.FirmTracks,
            HostileTracks = sim.LatestSnapshot.HostileTracks,
            HostileAircraft = sim.LatestSnapshot.HostileAircraft,
            ActiveMissiles = sim.LatestSnapshot.ActiveMissiles,
            ActiveEcmEffects = new[] { new RadarSystem.EcmEffect("jam-1", 42, 50000, 0.9) },
            AvailableWeapons = sim.LatestSnapshot.AvailableWeapons,
            SelectedWeapon = sim.LatestSnapshot.SelectedWeapon,
            TrackThreatStates = sim.LatestSnapshot.TrackThreatStates,
            RecentIncidents = sim.LatestSnapshot.RecentIncidents,
            RadarSweepAngle = sim.LatestSnapshot.RadarSweepAngle,
            RadarRangeNm = sim.LatestSnapshot.RadarRangeNm,
            RadarMode = sim.LatestSnapshot.RadarMode,
            Weather = sim.LatestSnapshot.Weather
        };

        var picture = OperationalPictureBuilder.Build(snapshot, scenario, supportPackages: support.Packages);

        Assert.Contains(picture.TacticalMarkers, marker => marker.Kind == TacticalMarkerKind.ObjectivePrimary);
        Assert.Contains(picture.TacticalMarkers, marker => marker.Kind == TacticalMarkerKind.FriendlySupport);
        Assert.Contains(picture.TacticalMarkers, marker => marker.Kind == TacticalMarkerKind.Jamming);
        Assert.Contains(picture.FriendlyForces, force => force.VisibleInPicture && force.MarkerClass == "support-cap");
    }
}
