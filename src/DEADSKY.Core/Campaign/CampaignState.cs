using DEADSKY.Core.Economy;
using DEADSKY.Core.Personnel;
using DEADSKY.Core.Progression;

namespace DEADSKY.Core.Campaign;

public sealed class CampaignState
{
    public int BudgetBalance { get; set; } = 25000;
    public int MissionsCompleted { get; set; }
    public int TotalKills { get; set; }
    public List<string> OwnedRequisitionIds { get; set; } = new();
    public List<PendingRequisitionDelivery> PendingDeliveries { get; set; } = new();
    public List<CrewMemberState> CrewStates { get; set; } = new();
    public List<SectorCampaignState> SectorStates { get; set; } = new();
    public string LastScenarioKey { get; set; } = "";
    public string LastScenarioName { get; set; } = "";
    public string LastScenarioArchetype { get; set; } = "";
    public string LastScenarioCategory { get; set; } = "";
    public bool LiveMissionResumeAvailable { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

public static class CampaignStateMapper
{
    public static CampaignState Capture(
        BudgetSystem budget,
        PlayerProfile profile,
        IEnumerable<string> ownedRequisitionIds,
        IEnumerable<PendingRequisitionDelivery>? pendingDeliveries = null,
        CrewRoster? crew = null,
        IEnumerable<SectorCampaignState>? sectorStates = null,
        string? lastScenarioKey = null,
        string? lastScenarioName = null,
        string? lastScenarioArchetype = null,
        string? lastScenarioCategory = null)
    {
        return new CampaignState
        {
            BudgetBalance = budget.Balance,
            MissionsCompleted = profile.MissionsCompleted,
            TotalKills = profile.TotalKills,
            OwnedRequisitionIds = ownedRequisitionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            PendingDeliveries = pendingDeliveries?.ToList() ?? new List<PendingRequisitionDelivery>(),
            CrewStates = crew?.CaptureState().ToList() ?? new List<CrewMemberState>(),
            SectorStates = sectorStates?.ToList() ?? new List<SectorCampaignState>(),
            LastScenarioKey = lastScenarioKey ?? "",
            LastScenarioName = lastScenarioName ?? "",
            LastScenarioArchetype = lastScenarioArchetype ?? "",
            LastScenarioCategory = lastScenarioCategory ?? "",
            LiveMissionResumeAvailable = false,
            LastUpdatedUtc = DateTime.UtcNow
        };
    }

    public static void Apply(
        CampaignState state,
        BudgetSystem budget,
        PlayerProfile profile,
        RequisitionTerminal requisitionTerminal,
        CrewRoster? crew = null)
    {
        budget.SetBalance(state.BudgetBalance);
        profile.LoadProgress(state.MissionsCompleted, state.TotalKills);
        requisitionTerminal.ReplaceOwnedItems(state.OwnedRequisitionIds);
        requisitionTerminal.ReplacePendingDeliveries(state.PendingDeliveries);
        crew?.ApplyState(state.CrewStates);
    }
}
