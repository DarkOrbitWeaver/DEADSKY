using DEADSKY.Core.Campaign;
using DEADSKY.Core.Comms;

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
}
