using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DEADSKY.Core.Economy;
using DEADSKY.Core.Progression;

namespace DEADSKY.App.ViewModels;

public partial class MainViewModel
{
    public RequisitionTerminal RequisitionTerminal { get; } = new();
    public ObservableCollection<RequisitionOptionViewModel> RequisitionOptions { get; } = new();

    public string BudgetDisplayText => $"OPERATIONAL BUDGET: {Budget.Balance:N0} OB";
    public string PlayerRankDisplayText => $"RANK: {Profile.Rank.ToString().ToUpperInvariant()}";
    public string CareerSummaryText => $"CAREER: {Profile.MissionsCompleted} MISSIONS | {Profile.TotalKills} KILLS";
    public string OwnedUpgradeSummaryText => RequisitionTerminal.BuildOwnedSummary();
    public string PendingDeliverySummaryText => RequisitionTerminal.BuildPendingSummary();
    public string ArmoryHeadlineText => RequisitionTerminal.PendingDeliveries.Count == 0
        ? "ARMORY STATUS: FIELD-READY"
        : $"ARMORY STATUS: {RequisitionTerminal.PendingDeliveries.Count} DELIVERY{(RequisitionTerminal.PendingDeliveries.Count == 1 ? string.Empty : "IES")} INBOUND";
    public string LogisticsTempoText => RequisitionTerminal.OwnedItemIds.Count == 0
        ? "No requisitioned improvements are installed yet. Use this menu to expand the station's reach, resilience, and coordination."
        : $"{RequisitionTerminal.OwnedItemIds.Count} requisitioned improvement{(RequisitionTerminal.OwnedItemIds.Count == 1 ? string.Empty : "s")} are already shaping live combat behavior.";
    public string LogisticsStatusText => RequisitionTerminal.PendingDeliveries.Count == 0
        ? "LOGISTICS STATUS: FIELD STOCK ON HAND."
        : "LOGISTICS STATUS: REMOTE DEPOT CONSIGNMENTS IN TRANSIT.";
    public string StoreAvailabilityText => SimulationRunning
        ? "REQUISITION LOCKED DURING ACTIVE COMBAT OPERATIONS."
        : "REQUISITION AVAILABLE WHILE THE BATTERY IS STANDBY OR PAUSED. SOME SYSTEMS ARRIVE AFTER FOLLOW-ON OPERATIONS.";

    private void InitializeRequisitionTerminal()
    {
        RequisitionOptions.Clear();
        foreach (var item in RequisitionTerminal.Catalog)
            RequisitionOptions.Add(new RequisitionOptionViewModel(item));

        RefreshRequisitionState();
    }

    [RelayCommand(CanExecute = nameof(CanPurchaseUpgrade))]
    private void PurchaseUpgrade(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return;

        var result = RequisitionTerminal.Purchase(itemId, Budget, Profile, Sim);
        SetStatus(result.Message);
        LogOps(result.Success ? "REQ" : "REQ-DENIED", result.Message);
        UpdateFromSnapshot(Sim.LatestSnapshot);
        RefreshRequisitionState();
        RefreshDerivedBindings();
        if (result.Success)
            SaveCampaignState();
    }

    private bool CanPurchaseUpgrade(string? itemId)
    {
        if (SimulationRunning || string.IsNullOrWhiteSpace(itemId))
            return false;

        var item = RequisitionTerminal.Catalog.FirstOrDefault(entry => entry.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        if (item == null || RequisitionTerminal.IsOwned(item.Id) || RequisitionTerminal.IsPending(item.Id))
            return false;

        if (Profile.Rank < item.MinimumRank)
            return false;

        return Budget.CanAfford(item.Cost);
    }

    private void RefreshRequisitionState()
    {
        foreach (var option in RequisitionOptions)
        {
            bool owned = RequisitionTerminal.IsOwned(option.Item.Id);
            bool pending = RequisitionTerminal.IsPending(option.Item.Id);
            bool rankLocked = Profile.Rank < option.Item.MinimumRank;
            bool affordable = Budget.CanAfford(option.Item.Cost);
            bool available = !SimulationRunning && !owned && !pending && !rankLocked && affordable;

            option.IsOwned = owned;
            option.IsRankLocked = rankLocked;
            option.IsAffordable = affordable;
            option.IsAvailable = available;
            option.StatusText = owned
                ? "INSTALLED"
                : pending
                    ? "IN TRANSIT"
                : rankLocked
                    ? $"LOCKED: {option.Item.MinimumRank.ToString().ToUpperInvariant()}"
                    : affordable
                        ? "READY FOR ISSUE"
                        : "INSUFFICIENT BUDGET";
            option.OrderActionText = owned
                ? "INSTALLED"
                : pending
                    ? "IN TRANSIT"
                    : available
                        ? "ORDER"
                        : rankLocked
                            ? "RANK LOCK"
                            : affordable
                                ? "STANDBY"
                                : "BUDGET LOW";
            option.RefreshComputedState();
        }

        PurchaseUpgradeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BudgetDisplayText));
        OnPropertyChanged(nameof(PlayerRankDisplayText));
        OnPropertyChanged(nameof(CareerSummaryText));
        OnPropertyChanged(nameof(OwnedUpgradeSummaryText));
        OnPropertyChanged(nameof(PendingDeliverySummaryText));
        OnPropertyChanged(nameof(ArmoryHeadlineText));
        OnPropertyChanged(nameof(LogisticsTempoText));
        OnPropertyChanged(nameof(LogisticsStatusText));
        OnPropertyChanged(nameof(StoreAvailabilityText));
    }

    private void ApplyOwnedRequisitionsToCurrentBattery()
    {
        RequisitionTerminal.ApplyOwnedUpgrades(Sim);
        RefreshRequisitionState();
    }

    private void ProcessPendingDeliveries()
    {
        var delivered = RequisitionTerminal.ProcessMissionTurnover(Profile, Sim);
        foreach (var item in delivered)
            LogOps("LOGISTICS", $"{item.DisplayName} delivered and installed after mission turnover.");

        if (delivered.Count > 0)
            SetStatus($"LOGISTICS UPDATE: {delivered.Count} DELIVERY{(delivered.Count == 1 ? "" : "IES")} RECEIVED");

        RefreshRequisitionState();
    }
}

public partial class RequisitionOptionViewModel : ObservableObject
{
    public RequisitionOptionViewModel(RequisitionItem item)
    {
        Item = item;
    }

    public RequisitionItem Item { get; }
    public string Id => Item.Id;
    public string DisplayName => Item.DisplayName;
    public string Category => Item.Category.ToString().ToUpperInvariant();
    public string CategoryBadgeText => Item.Category switch
    {
        RequisitionCategory.Radar => "SENSOR",
        RequisitionCategory.Missiles => "WEAPON",
        RequisitionCategory.Launchers => "LAUNCHER",
        RequisitionCategory.Electronics => "NETWORK",
        _ => Category
    };
    public string Description => Item.Description;
    public string CostText => $"{Item.Cost:N0} OB";
    public string RequirementText => Item.MinimumRank == PlayerRank.Lieutenant
        ? "STANDARD ISSUE"
        : $"REQUIRES {Item.MinimumRank.ToString().ToUpperInvariant()}";
    public string LeadTimeText => Item.LeadTimeMissions == 0
        ? "INSTALLS IMMEDIATELY"
        : Item.LeadTimeMissions == 1
            ? "ETA: AFTER 1 OPERATION"
            : $"ETA: AFTER {Item.LeadTimeMissions} OPERATIONS";
    public string ImpactText => Item.Id switch
    {
        "low_alt_module" => "Improves low-level detection and terrain-mask resistance in the live picture.",
        "eccm_suite" => "Cuts jamming pressure and stabilizes track quality under hostile ECM.",
        "rapid_reload" => "Reduces launcher turnaround so the battery can sustain longer fights.",
        "reserve_missile_crate" => "Adds depth to the magazine immediately for extended battles.",
        "long_range_missiles" => "Unlocks the outer-ring 48N6 shot for early raid breakup.",
        "ir_point_defense" => "Unlocks a passive close-defense option when radar support is degraded.",
        "proximity_frag_upgrade" => "Raises baseline lethality across every compatible missile shot.",
        "backup_power" => "Keeps the station fighting through power hits and partial disruption.",
        "hardened_comms" => "Improves comms resilience and support reliability during crisis traffic.",
        "data_link" => "Strengthens the shared operational picture for AI, command, and support tools.",
        "decoy_emitter" => "Improves support survivability against hostile targeting and retaliation.",
        _ => "Adds a live operational improvement to the station."
    };
    public string AvailabilityText => IsOwned
        ? "LIVE IN STATION"
        : IsRankLocked
            ? "RANK GATED"
            : IsAvailable
                ? "READY TO ORDER"
                : IsAffordable
                    ? "COMBAT LOCKED"
                    : "WAITING ON BUDGET";

    [ObservableProperty] private bool _isOwned;
    [ObservableProperty] private bool _isRankLocked;
    [ObservableProperty] private bool _isAffordable = true;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private string _statusText = "READY FOR ISSUE";
    [ObservableProperty] private string _orderActionText = "ORDER";

    public void RefreshComputedState()
    {
        OnPropertyChanged(nameof(AvailabilityText));
    }
}
