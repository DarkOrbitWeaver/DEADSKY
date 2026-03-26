using DEADSKY.Core.Economy;
using DEADSKY.Core.Progression;
using DEADSKY.Core.Weapons;

namespace DEADSKY.Backend.Tests;

public class RequisitionTerminalTests
{
    [Fact]
    public void Purchase_AppliesUpgradeAndSpendsBudget()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var budget = new BudgetSystem();
        var profile = new PlayerProfile();
        var terminal = new RequisitionTerminal();

        var result = terminal.Purchase("low_alt_module", budget, profile, sim);

        Assert.True(result.Success);
        Assert.True(sim.Entities.GetPlayerBattery()!.HasLowAltitudeModule);
        Assert.True(sim.Radar.Model.HasLowAltModule);
        Assert.True(budget.Balance < 25000);
    }

    [Fact]
    public void Purchase_DeniesDuplicateInstall()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var budget = new BudgetSystem();
        var profile = new PlayerProfile();
        var terminal = new RequisitionTerminal();

        var first = terminal.Purchase("rapid_reload", budget, profile, sim);
        var second = terminal.Purchase("rapid_reload", budget, profile, sim);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Contains("ALREADY", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Purchase_DeniesRankLockedItem_WhenProfileTooLow()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var budget = new BudgetSystem();
        var profile = new PlayerProfile();
        var terminal = new RequisitionTerminal();

        var result = terminal.Purchase("data_link", budget, profile, sim);

        Assert.False(result.Success);
        Assert.Contains("RANK", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(sim.Entities.GetPlayerBattery()!.HasDataLink);
    }

    [Fact]
    public void Purchase_QueuesDelayedDelivery_WhenItemRequiresLeadTime()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var budget = new BudgetSystem();
        var profile = new PlayerProfile();
        profile.LoadProgress(missionsCompleted: 3, totalKills: 5);

        var terminal = new RequisitionTerminal();
        var result = terminal.Purchase("data_link", budget, profile, sim);

        Assert.True(result.Success);
        Assert.Contains("ETA", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(terminal.IsOwned("data_link"));
        Assert.Single(terminal.PendingDeliveries);
        Assert.False(sim.Entities.GetPlayerBattery()!.HasDataLink);
    }

    [Fact]
    public void ProcessMissionTurnover_DeliversQueuedItems_WhenEtaIsReached()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var budget = new BudgetSystem();
        var profile = new PlayerProfile();
        profile.LoadProgress(missionsCompleted: 3, totalKills: 5);

        var terminal = new RequisitionTerminal();
        terminal.Purchase("data_link", budget, profile, sim);

        profile.LoadProgress(missionsCompleted: 4, totalKills: 5);
        var deliveredEarly = terminal.ProcessMissionTurnover(profile, sim);
        Assert.Empty(deliveredEarly);
        Assert.False(terminal.IsOwned("data_link"));

        profile.LoadProgress(missionsCompleted: 5, totalKills: 5);
        var delivered = terminal.ProcessMissionTurnover(profile, sim);

        Assert.Single(delivered);
        Assert.True(terminal.IsOwned("data_link"));
        Assert.Empty(terminal.PendingDeliveries);
        Assert.True(sim.Entities.GetPlayerBattery()!.HasDataLink);
    }

    [Fact]
    public void Purchase_LongRangeMissiles_UnlocksWeaponInBatteryLoadout()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var budget = new BudgetSystem();
        var profile = new PlayerProfile();
        profile.LoadProgress(missionsCompleted: 2, totalKills: 3);
        var terminal = new RequisitionTerminal();

        var result = terminal.Purchase("long_range_missiles", budget, profile, sim);
        profile.LoadProgress(missionsCompleted: 3, totalKills: 3);
        terminal.ProcessMissionTurnover(profile, sim);

        Assert.True(result.Success);
        Assert.Contains(WeaponCatalog.LongRangeSarhWeaponId, sim.Entities.GetPlayerBattery()!.AvailableWeaponIds);
    }
}
