using DEADSKY.Audio;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;

namespace DEADSKY.App.ViewModels;

public partial class MainViewModel
{
    private bool _shotReadyCueLatched;
    private string? _lastFireControlTrackId;

    private FireControlState CurrentFireControlState => AssessFireControlState(Sim.LatestSnapshot.Battery, SelectedTrack);

    public string TargetLockStatusText => CurrentFireControlState.LockStatusText;
    public string FireControlStatusText => CurrentFireControlState.FireStatusText;
    public string RangeEnvelopeStatusText => CurrentFireControlState.RangeStatusText;
    public string MissileFlightStatusText => CurrentSnapshot?.ActiveMissiles.Count > 0
        ? CurrentSnapshot.ActiveMissiles.Count == 1
            ? "MISSILE IN FLIGHT"
            : $"{CurrentSnapshot.ActiveMissiles.Count} MISSILES IN FLIGHT"
        : "RAILS COLD";
    public string BatteryLaunchStatusText => Sim.LatestSnapshot.Battery switch
    {
        null => "BATTERY SAFE",
        { ReadyLaunchers: > 0 } battery => $"{battery.ReadyLaunchers} READY",
        _ => "RELOAD"
    };
    public string FireControlHeadlineText => CurrentSnapshot?.ActiveMissiles.Count > 0
        ? MissileFlightStatusText
        : CurrentFireControlState.FireStatusText;
    public string FireControlSummaryText => $"{TargetLockStatusText} | {RangeEnvelopeStatusText} | {BatteryLaunchStatusText}";
    public string FireControlSupportText => CurrentFireControlState.HintText;

    private void RefreshFireControlFeedback(SimulationSnapshot snapshot)
    {
        var state = AssessFireControlState(snapshot.Battery, SelectedTrack);
        bool trackChanged = !string.Equals(_lastFireControlTrackId, SelectedTrack?.TrackId, StringComparison.Ordinal);

        if (state.IsShotReady && (trackChanged || !_shotReadyCueLatched))
        {
            Audio.Play(SoundEvent.WeaponReady);
            if (SelectedTrack != null)
            {
                PushNotification(
                    "FCR",
                    "SHOT READY",
                    $"{SelectedTrack.TrackId} is inside the launch envelope.",
                    NotificationSeverity.Info,
                    durationSeconds: 4);
            }
        }

        _shotReadyCueLatched = state.IsShotReady;
        _lastFireControlTrackId = SelectedTrack?.TrackId;

        OnPropertyChanged(nameof(TargetLockStatusText));
        OnPropertyChanged(nameof(FireControlStatusText));
        OnPropertyChanged(nameof(RangeEnvelopeStatusText));
        OnPropertyChanged(nameof(MissileFlightStatusText));
        OnPropertyChanged(nameof(BatteryLaunchStatusText));
        OnPropertyChanged(nameof(FireControlHeadlineText));
        OnPropertyChanged(nameof(FireControlSummaryText));
        OnPropertyChanged(nameof(FireControlSupportText));
        OnPropertyChanged(nameof(EngagementActionHintText));
    }

    private FireControlState AssessFireControlState(SAMBattery? battery, TrackFile? track)
    {
        if (!SimulationRunning || battery == null)
        {
            return new FireControlState(
                IsShotReady: false,
                IsInRange: false,
                LockStatusText: "SAFE",
                FireStatusText: "STANDBY",
                RangeStatusText: "NO TARGET",
                HintText: "Battery is safe. Start or resume the mission to arm radar tracking and fire controls.");
        }

        if (track == null)
        {
            return new FireControlState(
                IsShotReady: false,
                IsInRange: false,
                LockStatusText: "SEARCH",
                FireStatusText: "SELECT TARGET",
                RangeStatusText: "NO TARGET",
                HintText: "Select a contact on the scope or track board to enable designation and launch controls.");
        }

        bool hasLock = track.IsDesignated ||
                       (!string.IsNullOrWhiteSpace(battery.DesignatedTargetId) && battery.DesignatedTargetId == track.EntityId);
        bool rangeOk = track.RangeNm >= battery.MissileMinRangeNm && track.RangeNm <= battery.MissileMaxRangeNm;
        bool altitudeOk = track.AltitudeFt >= battery.MissileMinAltFt && track.AltitudeFt <= battery.MissileMaxAltFt;
        bool hasReadyLauncher = battery.ReadyLaunchers > 0;
        bool hostileClearance = battery.ROE switch
        {
            RulesOfEngagement.WeaponsFree => track.Classification != TrackClassification.Friendly,
            RulesOfEngagement.WeaponsHold => battery.IsUnderAttack,
            _ => track.Classification is TrackClassification.Hostile or TrackClassification.AssumedHostile
        };

        bool inRange = rangeOk && altitudeOk;
        bool shotReady = hasLock && inRange && hasReadyLauncher && hostileClearance;

        string lockStatus = hasLock ? "LOCK" : "NO LOCK";
        string fireStatus = !rangeOk
            ? track.RangeNm > battery.MissileMaxRangeNm
                ? "NO SHOT / LONG"
                : "NO SHOT / MIN RANGE"
            : !altitudeOk
                ? "NO SHOT / ALT"
                : !hasReadyLauncher
                    ? "NO SHOT / RELOAD"
                    : !hostileClearance
                        ? "NO SHOT / ROE"
                        : hasLock
                            ? "READY"
                            : "IN RANGE / DESIGNATE";

        string hint = !rangeOk
            ? track.RangeNm > battery.MissileMaxRangeNm
                ? "Track is outside max range. Hold the lock and let the target walk into the basket."
                : "Track is inside minimum range. Keep tracking until the geometry opens again."
            : !altitudeOk
                ? "Target is outside the altitude envelope. Maintain track and wait for a cleaner shot."
                : !hasReadyLauncher
                    ? "No ready launchers. Wait for reload or reserve recovery before engaging."
                    : !hostileClearance
                        ? battery.ROE == RulesOfEngagement.WeaponsHold
                            ? "Weapons hold is active. Maintain track until the battery is cleared to shoot."
                            : "Track is in range, but ROE still needs a hostile declaration."
                        : hasLock
                            ? "Fire-control solution is stable. Commit a single round or a quick salvo when ready."
                            : "Track is inside the weapon basket. Designate to steady the solution or fire to auto-designate.";

        string rangeStatus = inRange ? "IN RANGE" : "OUT OF RANGE";

        return new FireControlState(shotReady, inRange, lockStatus, fireStatus, rangeStatus, hint);
    }

    private sealed record FireControlState(
        bool IsShotReady,
        bool IsInRange,
        string LockStatusText,
        string FireStatusText,
        string RangeStatusText,
        string HintText);
}
