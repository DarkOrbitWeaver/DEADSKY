using DEADSKY.Core.Campaign;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Backend.Tests;

public class FriendlySupportDirectorTests
{
    [Fact]
    public void RequestSupport_PictureRelay_AcceptsAndQueuesFriendlyMessage()
    {
        var comms = new CommManager();
        var director = new FriendlySupportDirector(comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        var result = director.RequestSupport(
            FriendlySupportType.PictureRelay,
            "ALPHA ACTUAL",
            "Need refreshed picture.",
            10);

        var queued = SimulationTestFactory.DrainSingleQueuedMessage(comms);

        Assert.True(result.Accepted);
        Assert.Equal(RadioChannel.IntelNet, queued.Channel);
        Assert.Contains("SABLE", queued.DisplayHeader, StringComparison.OrdinalIgnoreCase);
        Assert.True(queued.CanReply);
    }

    [Fact]
    public void Tick_CombatAirPatrolCompletion_MakesSupportVisible()
    {
        var comms = new CommManager();
        var director = new FriendlySupportDirector(comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);

        Assert.Equal(SupportAvailabilityState.CoolingDown, cap.Availability);
        Assert.True(cap.IsVisibleInPicture);
    }

    [Fact]
    public void RequestSupport_DeclareCell_AcceptsAndQueuesIntelMessage()
    {
        var comms = new CommManager();
        var director = new FriendlySupportDirector(comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        var result = director.RequestSupport(
            FriendlySupportType.DeclarationCell,
            "ALPHA ACTUAL",
            "Need declare on TRK-0001.",
            15);

        var queued = SimulationTestFactory.DrainSingleQueuedMessage(comms);

        Assert.True(result.Accepted);
        Assert.Equal(RadioChannel.IntelNet, queued.Channel);
        Assert.Contains("ORACLE", queued.DisplayHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("declare", queued.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tick_JammingSupportCompletion_MakesSupportVisible()
    {
        var comms = new CommManager();
        var director = new FriendlySupportDirector(comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        director.RequestSupport(FriendlySupportType.JammingSupport, "ALPHA ACTUAL", "Need escort-jam.", 0);
        director.Tick(55, 55);

        var jammer = director.Packages.First(package => package.Type == FriendlySupportType.JammingSupport);

        Assert.Equal(SupportAvailabilityState.CoolingDown, jammer.Availability);
        Assert.True(jammer.IsVisibleInPicture);
    }

    [Fact]
    public void Tick_VisibleSupport_UpdatesTacticalPosition()
    {
        var comms = new CommManager();
        var director = new FriendlySupportDirector(comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);
        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        double firstBearing = cap.BearingDeg;
        double firstRange = cap.RangeNm;

        director.Tick(15, 85);

        Assert.True(cap.IsVisibleInPicture);
        Assert.True(cap.AltitudeFt > 10000);
        Assert.True(Math.Abs(cap.BearingDeg - firstBearing) > 0.1 || Math.Abs(cap.RangeNm - firstRange) > 0.1);
    }

    [Fact]
    public void UpdateOperationalContext_RaidPressureAndCriticalIncident_ReducesConfidence_AndAutoShowsSupportActors()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 40, rangeNm: 18);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "MiG-29", bearingDeg: 48, rangeNm: 23);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "Su-25", bearingDeg: 53, rangeNm: 27);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "EA-6", bearingDeg: 58, rangeNm: 34);
        sim.RefreshSnapshot();

        var director = new FriendlySupportDirector(sim.Comms);
        director.InitializeForScenario(scenario, null);
        director.UpdateOperationalContext(
            scenario,
            sim.LatestSnapshot,
            new[]
            {
                new EngagementIncident("blue_on_blue_warning", "Check fire", IncidentSeverity.Critical, DateTime.UtcNow)
            });

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        var awacs = director.Packages.First(package => package.Type == FriendlySupportType.Awacs);

        Assert.True(director.CommandConfidence < 1.0);
        Assert.True(cap.VisibleUntilSec > 0);
        Assert.True(awacs.VisibleUntilSec > 0);
        Assert.Contains("TRUST", director.LiveConsequenceSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequestSupport_HighRiskTaskingCanBeBlocked_WhenConfidenceCollapsed()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 40, rangeNm: 16);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "MiG-29", bearingDeg: 48, rangeNm: 18);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "Su-25", bearingDeg: 53, rangeNm: 22);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "EA-6", bearingDeg: 58, rangeNm: 28);
        sim.RefreshSnapshot();

        var director = new FriendlySupportDirector(sim.Comms);
        director.InitializeForScenario(scenario, null);
        director.UpdateOperationalContext(
            scenario,
            sim.LatestSnapshot,
            new[]
            {
                new EngagementIncident("friendly_fire_attempt", "Denied shot on friendly.", IncidentSeverity.Critical, DateTime.UtcNow),
                new EngagementIncident("blue_on_blue_warning", "Check fire", IncidentSeverity.Critical, DateTime.UtcNow)
            });

        var result = director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP now.", 10);

        Assert.False(result.Accepted);
        Assert.Contains("fire-discipline", result.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
