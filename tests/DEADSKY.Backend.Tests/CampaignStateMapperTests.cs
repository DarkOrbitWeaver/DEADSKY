using DEADSKY.Core.Campaign;
using DEADSKY.Core.Economy;
using DEADSKY.Core.Personnel;
using DEADSKY.Core.Progression;

namespace DEADSKY.Backend.Tests;

public class CampaignStateMapperTests
{
    [Fact]
    public void Capture_AndApply_RoundTripsBudgetProfileAndOwnedUpgrades()
    {
        var budget = new BudgetSystem();
        budget.Spend(3000, "test");

        var profile = new PlayerProfile();
        profile.LoadProgress(missionsCompleted: 4, totalKills: 9);

        var state = CampaignStateMapper.Capture(
            budget,
            profile,
            new[] { "low_alt_module", "rapid_reload" });

        var restoredBudget = new BudgetSystem();
        var restoredProfile = new PlayerProfile();
        var terminal = new RequisitionTerminal();

        CampaignStateMapper.Apply(state, restoredBudget, restoredProfile, terminal);

        Assert.Equal(budget.Balance, restoredBudget.Balance);
        Assert.Equal(4, restoredProfile.MissionsCompleted);
        Assert.Equal(9, restoredProfile.TotalKills);
        Assert.Equal(PlayerRank.Captain, restoredProfile.Rank);
        Assert.True(terminal.IsOwned("low_alt_module"));
        Assert.True(terminal.IsOwned("rapid_reload"));
    }

    [Fact]
    public void Capture_AndApply_RoundTripsPendingDeliveriesAndCrewState()
    {
        var budget = new BudgetSystem();
        var profile = new PlayerProfile();
        profile.LoadProgress(missionsCompleted: 5, totalKills: 11);

        var crew = CrewRoster.CreateDefaultCrew();
        crew.Soldiers[0].Morale = 0.48;
        crew.Soldiers[0].Fear = 0.35;
        crew.Soldiers[1].Health = HealthStatus.Wounded;

        var pending = new[]
        {
            new PendingRequisitionDelivery("data_link", "Data Link Terminal", requestedAtMission: 5, etaMission: 7)
        };
        var sectorStates = new[]
        {
            new SectorCampaignState
            {
                TheaterName = "Kovran Lowlands",
                MissionsFlown = 2,
                LastAfterActionSummary = "AFTER ACTION: DEPOT HELD.",
                Objectives = new List<ObjectiveCampaignState>
                {
                    new() { ObjectiveId = "depot", ObjectiveName = "Kovran Depot", IntegrityPct = 0.88, LastCondition = ObjectiveCondition.Threatened }
                }
            }
        };

        var state = CampaignStateMapper.Capture(
            budget,
            profile,
            new[] { "low_alt_module" },
            pending,
            crew,
            sectorStates);

        var restoredBudget = new BudgetSystem();
        var restoredProfile = new PlayerProfile();
        var restoredTerminal = new RequisitionTerminal();
        var restoredCrew = CrewRoster.CreateDefaultCrew();

        CampaignStateMapper.Apply(state, restoredBudget, restoredProfile, restoredTerminal, restoredCrew);

        Assert.True(restoredTerminal.IsOwned("low_alt_module"));
        Assert.Single(restoredTerminal.PendingDeliveries);
        Assert.Equal("data_link", restoredTerminal.PendingDeliveries[0].ItemId);
        Assert.Equal(0.48, restoredCrew.Soldiers[0].Morale, 2);
        Assert.Equal(0.35, restoredCrew.Soldiers[0].Fear, 2);
        Assert.Equal(HealthStatus.Wounded, restoredCrew.Soldiers[1].Health);
        Assert.Single(state.SectorStates);
        Assert.Equal("Kovran Lowlands", state.SectorStates[0].TheaterName);
        Assert.Equal(0.88, state.SectorStates[0].Objectives[0].IntegrityPct, 2);
    }

    [Fact]
    public void Capture_PersistsLastScenarioMetadata_WithoutClaimingLiveMissionResume()
    {
        var budget = new BudgetSystem();
        var profile = new PlayerProfile();

        var state = CampaignStateMapper.Capture(
            budget,
            profile,
            Array.Empty<string>(),
            lastScenarioKey: "operation",
            lastScenarioName: "Operation DEADSKY",
            lastScenarioArchetype: "multi_axis_timed_raid",
            lastScenarioCategory: "operation");

        Assert.Equal("operation", state.LastScenarioKey);
        Assert.Equal("Operation DEADSKY", state.LastScenarioName);
        Assert.Equal("multi_axis_timed_raid", state.LastScenarioArchetype);
        Assert.Equal("operation", state.LastScenarioCategory);
        Assert.False(state.LiveMissionResumeAvailable);
    }
}
