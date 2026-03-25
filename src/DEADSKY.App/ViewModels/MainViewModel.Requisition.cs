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
        }

        PurchaseUpgradeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BudgetDisplayText));
        OnPropertyChanged(nameof(PlayerRankDisplayText));
        OnPropertyChanged(nameof(CareerSummaryText));
        OnPropertyChanged(nameof(OwnedUpgradeSummaryText));
        OnPropertyChanged(nameof(PendingDeliverySummaryText));
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
    public string Description => Item.Description;
    public string CostText => $"{Item.Cost:N0} OB";
    public string RequirementText => Item.MinimumRank == PlayerRank.Lieutenant
        ? "STANDARD ISSUE"
        : $"REQUIRES {Item.MinimumRank.ToString().ToUpperInvariant()}";

    [ObservableProperty] private bool _isOwned;
    [ObservableProperty] private bool _isRankLocked;
    [ObservableProperty] private bool _isAffordable = true;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private string _statusText = "READY FOR ISSUE";
}
