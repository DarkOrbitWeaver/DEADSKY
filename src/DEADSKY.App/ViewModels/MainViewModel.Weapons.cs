using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DEADSKY.Audio;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;
using DEADSKY.Core.Weapons;

namespace DEADSKY.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string _selectedWeaponId = WeaponCatalog.BaselineSarhWeaponId;

    public ObservableCollection<WeaponOptionViewModel> WeaponOptions { get; } = new();
    public ObservableCollection<IncidentCardViewModel> RecentIncidentCards { get; } = new();

    public SharedOperationalPicture OperationalPicture =>
        OperationalPictureBuilder.Build(
            CurrentSnapshot ?? Sim.LatestSnapshot,
            CurrentScenarioDefinition,
            Sim.Weapons.Incidents,
            FriendlySupport.Packages,
            SelectedTrackId,
            FriendlySupport.CommandConfidence,
            FriendlySupport.CommandPostureSummary,
            FriendlySupport.LiveConsequenceSummary);

    private SharedOperationalPicture CurrentOperationalPicture => OperationalPicture;

    public string WeaponLoadoutSummaryText => CurrentSnapshot?.SelectedWeapon == null
        ? "LOADOUT: NO WEAPON SELECTED"
        : $"LOADOUT: {CurrentSnapshot.SelectedWeapon.DisplayName} // {CurrentSnapshot.SelectedWeapon.ShortCode}";

    public string SelectedWeaponDisplayText => CurrentSnapshot?.SelectedWeapon?.DisplayName ?? "9M38 Medium-Range SAM";
    public string SelectedWeaponGuidanceText => CurrentSnapshot?.SelectedWeapon == null
        ? "GUIDANCE: STANDBY"
        : $"GUIDANCE: {CurrentSnapshot.SelectedWeapon.GuidanceMode.ToString().ToUpperInvariant()}";
    public string SelectedWeaponEnvelopeText => CurrentSnapshot?.SelectedWeapon == null
        ? "ENVELOPE: ---"
        : $"ENVELOPE: {CurrentSnapshot.SelectedWeapon.MinRangeNm * 1.852:0.0}-{CurrentSnapshot.SelectedWeapon.MaxRangeNm * 1.852:0.0}km | {CurrentSnapshot.SelectedWeapon.MinAltitudeFt * 0.3048:0}-{CurrentSnapshot.SelectedWeapon.MaxAltitudeFt * 0.3048:0}m";
    public string SelectedWeaponCountermeasureText => CurrentSnapshot?.SelectedWeapon == null
        ? "COUNTERMEASURES: ---"
        : WeaponCatalog.BuildCountermeasureRiskText(CurrentSnapshot.SelectedWeapon).ToUpperInvariant();
    public string SelectedWeaponSupportText => CurrentSnapshot?.SelectedWeapon == null
        ? "SUPPORT: ---"
        : CurrentSnapshot.SelectedWeapon.RequiresRadarSupport
            ? "SUPPORT: RADAR TRACK REQUIRED"
            : "SUPPORT: PASSIVE / IR CAPABLE";
    public string SelectedWeaponDescriptionText => CurrentSnapshot?.SelectedWeapon?.Description ?? "Baseline medium-range radar-guided missile.";
    public string SelectedWeaponReadinessText => CurrentSnapshot?.Battery == null
        ? "MAGAZINE: STANDBY"
        : $"MAGAZINE: {CurrentSnapshot.Battery.ReadyLaunchers} READY | {CurrentSnapshot.Battery.ReserveMissiles} RESERVE";
    public string SelectedWeaponPkText => BuildSelectedWeaponPkText();
    public string WeaponRecommendationText => BuildWeaponRecommendationText();
    public string WeaponActionStatusText => BuildWeaponActionStatusText();
    public string FriendlyFireRiskText => BuildFriendlyFireRiskText();
    public string OperationalPictureText => $"WORLD: {CurrentOperationalPicture.ThreatSummary} {CurrentOperationalPicture.SupportSummary}";
    public string RecentIncidentSummaryText => CurrentOperationalPicture.ConsequenceSummary;
    public string RecommendedActionSummaryText => CurrentOperationalPicture.RecommendedActionSummary;
    public string TacticalMapTitleText => CurrentOperationalPicture.ScenarioHeader;
    public string TacticalMapNotesText => CurrentOperationalPicture.ScenarioNotes;
    public IReadOnlyList<ObjectiveTacticalState> TacticalObjectives => CurrentOperationalPicture.ObjectiveStates;
    public string ObjectiveFocusText => TacticalObjectives.Count == 0
        ? "OBJECTIVE FOCUS: NO ACTIVE OBJECTIVES"
        : "OBJECTIVE FOCUS: " + string.Join(" | ", TacticalObjectives.Select(objective => $"{objective.Name.ToUpperInvariant()} {objective.Status}"));
    public string CommsConsequenceText => CurrentOperationalPicture.CommsConsequences.OutstandingWarning;
    public string CommandTrustText => CurrentOperationalPicture.CommsConsequences.CommandTrustSummary;
    public string RecentIncidentHeadlineText => RecentIncidentCards.Count == 0
        ? "CONSEQUENCE FEED: QUIET"
        : $"CONSEQUENCE FEED: {RecentIncidentCards.Count} LIVE FLAG{(RecentIncidentCards.Count == 1 ? string.Empty : "S")}";
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
    public void SelectWeapon(string weaponId)
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

        RefreshRecentIncidentCards();
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
        OnPropertyChanged(nameof(SelectedWeaponReadinessText));
        OnPropertyChanged(nameof(SelectedWeaponPkText));
        OnPropertyChanged(nameof(WeaponRecommendationText));
        OnPropertyChanged(nameof(WeaponActionStatusText));
        OnPropertyChanged(nameof(FriendlyFireRiskText));
        OnPropertyChanged(nameof(OperationalPictureText));
        OnPropertyChanged(nameof(RecentIncidentSummaryText));
        OnPropertyChanged(nameof(RecommendedActionSummaryText));
        OnPropertyChanged(nameof(TacticalMapTitleText));
        OnPropertyChanged(nameof(TacticalMapNotesText));
        OnPropertyChanged(nameof(TacticalObjectives));
        OnPropertyChanged(nameof(ObjectiveFocusText));
        OnPropertyChanged(nameof(CommsConsequenceText));
        OnPropertyChanged(nameof(CommandTrustText));
        OnPropertyChanged(nameof(SupportRecommendationText));
        OnPropertyChanged(nameof(RecommendedSupportCommandKey));
        OnPropertyChanged(nameof(RecommendedSupportActionText));
        OnPropertyChanged(nameof(RecentIncidentHeadlineText));
        OnPropertyChanged(nameof(VisibleFriendlyForces));
        OnPropertyChanged(nameof(VisibleFriendlyForceText));
        OnPropertyChanged(nameof(AbortAvailabilityText));
        OnPropertyChanged(nameof(OperationalPicture));
    }

    private void RefreshRecentIncidentCards()
    {
        var incidents = Sim.Weapons.Incidents
            .OrderByDescending(incident => incident.TimestampUtc)
            .Take(3)
            .ToList();

        for (int i = 0; i < incidents.Count; i++)
        {
            if (i < RecentIncidentCards.Count)
                RecentIncidentCards[i].Update(incidents[i]);
            else
                RecentIncidentCards.Add(new IncidentCardViewModel(incidents[i]));
        }

        while (RecentIncidentCards.Count > incidents.Count)
            RecentIncidentCards.RemoveAt(RecentIncidentCards.Count - 1);
    }

    private string BuildSelectedWeaponPkText()
    {
        if (CurrentSnapshot?.Battery == null || CurrentSnapshot.SelectedWeapon == null)
            return "PK WINDOW: STANDBY";

        if (SelectedTrack == null)
            return $"PK WINDOW: {CurrentSnapshot.SelectedWeapon.BaseSingleShotPk:P0} BASELINE";

        double singlePk = EstimateWeaponPk(CurrentSnapshot.SelectedWeapon, SelectedTrack);
        double salvoPk = 1.0 - Math.Pow(1.0 - singlePk, 2);
        return $"PK WINDOW: {singlePk:P0} SINGLE | {salvoPk:P0} SALVO";
    }

    private string BuildWeaponRecommendationText()
    {
        if (CurrentSnapshot?.Battery == null)
            return "RECOMMENDATION: BATTERY SAFE";

        if (SelectedTrack == null)
            return "RECOMMENDATION: SORT CONTACTS, THEN SELECT A WEAPON FOR THE CURRENT GEOMETRY.";

        var candidates = CurrentSnapshot.AvailableWeapons
            .Select(weapon => new
            {
                Weapon = weapon,
                Valid = CanEmployWeapon(weapon, SelectedTrack, out string reason),
                Reason = reason,
                Pk = EstimateWeaponPk(weapon, SelectedTrack)
            })
            .ToList();

        var validCandidate = candidates
            .Where(candidate => candidate.Valid)
            .OrderByDescending(candidate => candidate.Pk)
            .ThenByDescending(candidate => candidate.Weapon.MaxRangeNm)
            .FirstOrDefault();

        if (validCandidate == null)
        {
            string blocker = candidates.FirstOrDefault()?.Reason ?? "NO COMPATIBLE SHOT";
            return $"RECOMMENDATION: HOLD FIRE. {blocker.ToUpperInvariant()}";
        }

        string employment = validCandidate.Weapon.GuidanceMode == GuidanceMode.Infrared
            ? "quiet close-in snap shot"
            : validCandidate.Weapon.SupportsTerminalHandoff
                ? "outer-ring first shot"
                : "main battery intercept";
        return $"RECOMMENDATION: {validCandidate.Weapon.ShortCode} FOR {employment.ToUpperInvariant()} ({validCandidate.Pk:P0} EST.).";
    }

    private string BuildWeaponActionStatusText()
    {
        if (SelectedTrack == null || CurrentSnapshot?.SelectedWeapon == null)
            return "SHOT GATE: SELECT A TRACK TO SEE LIVE EMPLOYMENT CONSTRAINTS.";

        bool valid = CanEmployWeapon(CurrentSnapshot.SelectedWeapon, SelectedTrack, out string reason);
        return valid
            ? "SHOT GATE: CURRENT LOADOUT IS CLEARED FOR THIS TARGET."
            : $"SHOT GATE: {reason.ToUpperInvariant()}";
    }

    private string BuildFriendlyFireRiskText()
    {
        if (SelectedTrack == null)
            return "IDENT CHECK: NO TRACK SELECTED";

        return SelectedTrack.Classification switch
        {
            TrackClassification.Friendly => "IDENT CHECK: FRIENDLY TRACK. FIRING WILL TRIGGER A CRITICAL INCIDENT.",
            TrackClassification.Civilian => "IDENT CHECK: CIVILIAN OR NON-COMBATANT TRAFFIC. HOLD FIRE.",
            TrackClassification.Unknown => "IDENT CHECK: UNKNOWN CONTACT. DECLARE OR HOLD UNTIL HOSTILE CRITERIA ARE MET.",
            _ => CurrentSnapshot?.Battery?.ROE == RulesOfEngagement.WeaponsTight &&
                 SelectedTrack.Classification != TrackClassification.Hostile &&
                 SelectedTrack.Classification != TrackClassification.AssumedHostile
                ? "IDENT CHECK: WEAPONS TIGHT. HOSTILE DECLARATION STILL REQUIRED."
                : "IDENT CHECK: HOSTILE CRITERIA SATISFIED FOR CURRENT ROE."
        };
    }

    private bool CanEmployWeapon(WeaponDefinition weapon, TrackFile track, out string reason)
    {
        reason = "NO SHOT";

        if (CurrentSnapshot?.Battery == null)
        {
            reason = "battery offline";
            return false;
        }

        if (track.Classification == TrackClassification.Friendly || track.Classification == TrackClassification.Civilian)
        {
            reason = "identity check failed";
            return false;
        }

        if (track.RangeNm < weapon.MinRangeNm)
        {
            reason = "inside minimum range";
            return false;
        }

        if (track.RangeNm > weapon.MaxRangeNm)
        {
            reason = "outside maximum range";
            return false;
        }

        if (track.AltitudeFt < weapon.MinAltitudeFt || track.AltitudeFt > weapon.MaxAltitudeFt)
        {
            reason = "outside altitude envelope";
            return false;
        }

        if (CurrentSnapshot.Battery.ReadyLaunchers <= 0)
        {
            reason = "reload in progress";
            return false;
        }

        if (weapon.RequiresRadarSupport && !HasTrackSupport(track))
        {
            reason = "radar support not established";
            return false;
        }

        if (weapon.GuidanceMode == GuidanceMode.Infrared &&
            track.AspectString.Contains("COLD", StringComparison.OrdinalIgnoreCase) &&
            track.RangeNm > 4.5)
        {
            reason = "ir seeker weak on cold aspect";
            return false;
        }

        if (CurrentSnapshot.Battery.ROE == RulesOfEngagement.WeaponsHold && !CurrentSnapshot.Battery.IsUnderAttack)
        {
            reason = "weapons hold active";
            return false;
        }

        if (CurrentSnapshot.Battery.ROE == RulesOfEngagement.WeaponsTight &&
            track.Classification is not (TrackClassification.Hostile or TrackClassification.AssumedHostile))
        {
            reason = "roe requires hostile declaration";
            return false;
        }

        reason = "shot valid";
        return true;
    }

    private bool HasTrackSupport(TrackFile track)
    {
        if (CurrentSnapshot?.Battery == null)
            return false;

        return track.IsDesignated ||
               (CurrentSnapshot.Battery.RadarMode == RadarMode.TrackWhileScan && track.IsTrackHeld);
    }

    private double EstimateWeaponPk(WeaponDefinition weapon, TrackFile track)
    {
        double pk = weapon.BaseSingleShotPk;
        double rangeFactor = (track.RangeNm - weapon.MinRangeNm) / Math.Max(0.1, weapon.MaxRangeNm - weapon.MinRangeNm);
        rangeFactor = Math.Clamp(rangeFactor, 0.0, 1.0);
        double rangeModifier = 1.0 - Math.Abs(rangeFactor - 0.4) * 0.5;

        double countermeasureMod = 1.0;
        if (track.EntityId != null && CurrentSnapshot != null)
        {
            var aircraft = CurrentSnapshot.HostileAircraft.FirstOrDefault(entity => entity.Id == track.EntityId);
            if (aircraft != null)
            {
                if (weapon.SusceptibleToChaff && (aircraft.ECMActive || aircraft.IsChaffActive))
                    countermeasureMod *= 0.68;
                if (weapon.SusceptibleToFlares && aircraft.IsFlareActive)
                    countermeasureMod *= 0.62;
            }
        }

        double altitudeModifier = track.AltitudeFt < 500 ? 0.5 : 1.0;
        return Math.Clamp(pk * rangeModifier * countermeasureMod * altitudeModifier, 0.05, 0.97);
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
    [ObservableProperty] private string _envelopeLabel = "";
    [ObservableProperty] private string _supportLabel = "";
    [ObservableProperty] private string _pkLabel = "";
    [ObservableProperty] private string _countermeasureLabel = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _tooltip = "";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _selectLabel = "SELECT";

    public void Update(WeaponDefinition definition, bool isSelected)
    {
        Id = definition.Id;
        DisplayName = $"{definition.ShortCode} // {definition.DisplayName}";
        GuidanceLabel = definition.GuidanceMode.ToString().ToUpperInvariant();
        RangeLabel = $"{definition.MinRangeNm * 1.852:0.0}-{definition.MaxRangeNm * 1.852:0.0}km";
        EnvelopeLabel = $"{definition.MinAltitudeFt * 0.3048:0}-{definition.MaxAltitudeFt * 0.3048:0}m";
        SupportLabel = definition.RequiresRadarSupport ? "RADAR SUPPORT" : "PASSIVE / IR";
        PkLabel = $"{definition.BaseSingleShotPk:P0} BASE PK";
        CountermeasureLabel = definition.SusceptibleToChaff
            ? "CHAFF RISK"
            : definition.SusceptibleToFlares
                ? "FLARE RISK"
                : "LOW CM RISK";
        Description = definition.Description;
        Tooltip = definition.Tooltip;
        IsSelected = isSelected;
        SelectLabel = isSelected ? "SELECTED" : "SELECT";
    }
}

public partial class IncidentCardViewModel : ObservableObject
{
    public IncidentCardViewModel(EngagementIncident incident)
    {
        Update(incident);
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _timeText = "";
    [ObservableProperty] private string _severityLabel = "";

    public void Update(EngagementIncident incident)
    {
        Title = incident.IncidentType.Replace('_', ' ').ToUpperInvariant();
        Summary = incident.Summary;
        TimeText = $"{incident.TimestampUtc:HH:mm:ss}Z";
        SeverityLabel = incident.Severity.ToString().ToUpperInvariant();
    }
}
