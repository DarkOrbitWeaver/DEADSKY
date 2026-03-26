using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DEADSKY.Audio;
using DEADSKY.Core.Simulation;
using DEADSKY.Core.Weapons;

namespace DEADSKY.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string _selectedWeaponId = WeaponCatalog.BaselineSarhWeaponId;

    public ObservableCollection<WeaponOptionViewModel> WeaponOptions { get; } = new();

    private SharedOperationalPicture CurrentOperationalPicture =>
        OperationalPictureBuilder.Build(CurrentSnapshot ?? Sim.LatestSnapshot, Sim.Weapons.Incidents, FriendlySupport.Packages);

    public string WeaponLoadoutSummaryText => CurrentSnapshot?.SelectedWeapon == null
        ? "LOADOUT: NO WEAPON SELECTED"
        : $"LOADOUT: {CurrentSnapshot.SelectedWeapon.DisplayName} // {CurrentSnapshot.SelectedWeapon.ShortCode}";

    public string SelectedWeaponDisplayText => CurrentSnapshot?.SelectedWeapon?.DisplayName ?? "9M38 Medium-Range SAM";
    public string SelectedWeaponGuidanceText => CurrentSnapshot?.SelectedWeapon == null
        ? "GUIDANCE: STANDBY"
        : $"GUIDANCE: {CurrentSnapshot.SelectedWeapon.GuidanceMode.ToString().ToUpperInvariant()}";
    public string SelectedWeaponEnvelopeText => CurrentSnapshot?.SelectedWeapon == null
        ? "ENVELOPE: ---"
        : $"ENVELOPE: {CurrentSnapshot.SelectedWeapon.MinRangeNm:0.0}-{CurrentSnapshot.SelectedWeapon.MaxRangeNm:0.0}NM | {CurrentSnapshot.SelectedWeapon.MinAltitudeFt:0}-{CurrentSnapshot.SelectedWeapon.MaxAltitudeFt:0}FT";
    public string SelectedWeaponCountermeasureText => CurrentSnapshot?.SelectedWeapon == null
        ? "COUNTERMEASURES: ---"
        : WeaponCatalog.BuildCountermeasureRiskText(CurrentSnapshot.SelectedWeapon).ToUpperInvariant();
    public string SelectedWeaponSupportText => CurrentSnapshot?.SelectedWeapon == null
        ? "SUPPORT: ---"
        : CurrentSnapshot.SelectedWeapon.RequiresRadarSupport
            ? "SUPPORT: RADAR TRACK REQUIRED"
            : "SUPPORT: PASSIVE / IR CAPABLE";
    public string SelectedWeaponDescriptionText => CurrentSnapshot?.SelectedWeapon?.Description ?? "Baseline medium-range radar-guided missile.";
    public string OperationalPictureText => $"WORLD: {CurrentOperationalPicture.ThreatSummary} {CurrentOperationalPicture.SupportSummary}";
    public string RecentIncidentSummaryText => CurrentOperationalPicture.ConsequenceSummary;
    public IReadOnlyList<FriendlyForceState> VisibleFriendlyForces => CurrentOperationalPicture.FriendlyForces;
    public string VisibleFriendlyForceText => CurrentOperationalPicture.FriendlyForces.Count == 0
        ? "FRIENDLIES: NO ACTIVE SUPPORT TRACKS"
        : "FRIENDLIES: " + string.Join(" | ", CurrentOperationalPicture.FriendlyForces
            .Where(force => force.VisibleInPicture)
            .Select(force => $"{force.Callsign} {force.Status}")
            .DefaultIfEmpty("NO ACTIVE SUPPORT TRACKS"));
    public string AbortAvailabilityText => SelectedTrackId == null
        ? "ABORT: NO TRACK SELECTED"
        : Sim.Weapons.CanAbortTrack(SelectedTrackId)
            ? "ABORT: SELECTIVE SELF-DESTRUCT AVAILABLE"
            : "ABORT: WEAPON COMMIT OR NO ABORT PATH";

    [RelayCommand(CanExecute = nameof(CanSelectWeapon))]
    private void SelectWeapon(string weaponId)
    {
        if (string.IsNullOrWhiteSpace(weaponId))
            return;

        if (!Sim.PlayerSelectWeapon(weaponId))
            return;

        SelectedWeaponId = weaponId;
        RefreshFromSimulationSnapshot();
        SetStatus($"LOADOUT SET: {SelectedWeaponDisplayText.ToUpperInvariant()}");
        Audio.Play(SoundEvent.UiButtonPress);
        RefreshWeaponPresentation();
    }

    private bool CanSelectWeapon(string? weaponId) =>
        !string.IsNullOrWhiteSpace(weaponId) &&
        CurrentSnapshot?.AvailableWeapons.Any(weapon => weapon.Id.Equals(weaponId, StringComparison.OrdinalIgnoreCase)) == true;

    [RelayCommand(CanExecute = nameof(CanAbortSelectedTrack))]
    private void AbortSelectedTrack()
    {
        if (SelectedTrackId == null)
            return;

        int aborted = Sim.PlayerAbortTrack(SelectedTrackId);
        RefreshFromSimulationSnapshot();
        SetStatus(aborted > 0
            ? $"ABORT ORDERED: {SelectedTrackId}"
            : $"ABORT UNAVAILABLE: {SelectedTrackId}");
        RefreshWeaponPresentation();
    }

    private bool CanAbortSelectedTrack() =>
        SimulationRunning && SelectedTrackId != null && Sim.Weapons.CanAbortTrack(SelectedTrackId);

    internal void RefreshWeaponState(SimulationSnapshot snapshot)
    {
        SelectedWeaponId = snapshot.SelectedWeapon?.Id ?? WeaponCatalog.BaselineSarhWeaponId;

        for (int i = 0; i < snapshot.AvailableWeapons.Count; i++)
        {
            WeaponDefinition definition = snapshot.AvailableWeapons[i];
            if (i < WeaponOptions.Count)
                WeaponOptions[i].Update(definition, definition.Id.Equals(SelectedWeaponId, StringComparison.OrdinalIgnoreCase));
            else
                WeaponOptions.Add(new WeaponOptionViewModel(definition, definition.Id.Equals(SelectedWeaponId, StringComparison.OrdinalIgnoreCase)));
        }

        while (WeaponOptions.Count > snapshot.AvailableWeapons.Count)
            WeaponOptions.RemoveAt(WeaponOptions.Count - 1);

        RefreshWeaponPresentation();
    }

    internal void RefreshWeaponPresentation()
    {
        SelectWeaponCommand.NotifyCanExecuteChanged();
        AbortSelectedTrackCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(WeaponLoadoutSummaryText));
        OnPropertyChanged(nameof(SelectedWeaponDisplayText));
        OnPropertyChanged(nameof(SelectedWeaponGuidanceText));
        OnPropertyChanged(nameof(SelectedWeaponEnvelopeText));
        OnPropertyChanged(nameof(SelectedWeaponCountermeasureText));
        OnPropertyChanged(nameof(SelectedWeaponSupportText));
        OnPropertyChanged(nameof(SelectedWeaponDescriptionText));
        OnPropertyChanged(nameof(OperationalPictureText));
        OnPropertyChanged(nameof(RecentIncidentSummaryText));
        OnPropertyChanged(nameof(VisibleFriendlyForces));
        OnPropertyChanged(nameof(VisibleFriendlyForceText));
        OnPropertyChanged(nameof(AbortAvailabilityText));
    }
}

public partial class WeaponOptionViewModel : ObservableObject
{
    public WeaponOptionViewModel(WeaponDefinition definition, bool isSelected)
    {
        Update(definition, isSelected);
    }

    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _guidanceLabel = "";
    [ObservableProperty] private string _rangeLabel = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _tooltip = "";
    [ObservableProperty] private bool _isSelected;

    public void Update(WeaponDefinition definition, bool isSelected)
    {
        Id = definition.Id;
        DisplayName = $"{definition.ShortCode} // {definition.DisplayName}";
        GuidanceLabel = definition.GuidanceMode.ToString().ToUpperInvariant();
        RangeLabel = $"{definition.MinRangeNm:0.0}-{definition.MaxRangeNm:0.0}NM";
        Description = definition.Description;
        Tooltip = definition.Tooltip;
        IsSelected = isSelected;
    }
}
