using DEADSKY.App.Services;
using DEADSKY.Core.Campaign;
using CommunityToolkit.Mvvm.Input;

namespace DEADSKY.App.ViewModels;

public partial class MainViewModel
{
    private readonly CampaignPersistenceService _campaignPersistence = new();
    private CampaignState? _loadedCampaignState;

    public string CampaignSavePathText => $"CAMPAIGN SAVE: {_campaignPersistence.SavePath}";
    public string CampaignPersistenceSummaryText
    {
        get
        {
            string summary = "SAVES PROFILE, CAMPAIGN, LOGISTICS, CREW STATE, AND THEATER CONSEQUENCES. LIVE MISSION STATE DOES NOT RESUME.";
            if (_loadedCampaignState == null)
                return summary;

            string archetype = string.IsNullOrWhiteSpace(_loadedCampaignState.LastScenarioArchetype)
                ? "UNSPECIFIED OPERATION"
                : _loadedCampaignState.LastScenarioArchetype.Replace('_', ' ').ToUpperInvariant();
            string lastPlayed = string.IsNullOrWhiteSpace(_loadedCampaignState.LastScenarioName)
                ? "LAST PLAYED: NO RECORDED OPERATION."
                : $"LAST PLAYED: {_loadedCampaignState.LastScenarioName.ToUpperInvariant()} // {archetype}.";
            string updated = $"UPDATED: {_loadedCampaignState.LastUpdatedUtc:yyyy-MM-dd HH:mm} UTC.";
            return $"{summary} {lastPlayed} {updated}";
        }
    }

    private void LoadCampaignState()
    {
        var state = _campaignPersistence.Load();
        _loadedCampaignState = state;
        if (state == null)
        {
            SetStatus("CAMPAIGN PROFILE READY");
            return;
        }

        CampaignStateMapper.Apply(state, Budget, Profile, RequisitionTerminal, Crew);
        _sectorStates.Clear();
        _sectorStates.AddRange(state.SectorStates);
        RefreshRequisitionState();
        RefreshCrewDisplay();
        RefreshSectorStateDisplay();
        RefreshDerivedBindings();
        SetStatus($"CAMPAIGN PROFILE LOADED // {state.MissionsCompleted} MISSIONS");
    }

    public void SaveCampaignState()
    {
        var state = CampaignStateMapper.Capture(
            Budget,
            Profile,
            RequisitionTerminal.OwnedItemIds,
            RequisitionTerminal.PendingDeliveries,
            Crew,
            _sectorStates,
            CurrentScenarioDefinition?.Key,
            CurrentScenarioDefinition?.Name,
            CurrentScenarioDefinition?.Archetype,
            CurrentScenarioDefinition?.Category);

        bool saved = _campaignPersistence.Save(state);
        if (saved)
        {
            _loadedCampaignState = state;
            OnPropertyChanged(nameof(CampaignSavePathText));
            OnPropertyChanged(nameof(CampaignPersistenceSummaryText));
        }
    }

    [RelayCommand]
    private void SaveCampaignNow()
    {
        SaveCampaignState();
        SetStatus("CAMPAIGN PROFILE SAVED");
        LogOps("SAVE", "Campaign profile saved to local storage.");
        OnPropertyChanged(nameof(CampaignPersistenceSummaryText));
    }
}
