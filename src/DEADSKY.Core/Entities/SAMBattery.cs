using DEADSKY.Core.Physics;
using DEADSKY.Core.Weapons;

namespace DEADSKY.Core.Entities;

public enum LauncherState
{
    Ready,
    Reloading,
    Empty,
    Damaged,
    Offline
}

public enum BatteryAlertLevel
{
    Green,    // Peacetime
    Yellow,   // Elevated
    Orange,   // High
    Red,      // Imminent
    Black     // Under attack
}

public enum RulesOfEngagement
{
    WeaponsFree,    // Shoot any non-friendly
    WeaponsTight,   // Shoot only confirmed hostile
    WeaponsHold     // Self-defense only
}

public enum RadarMode
{
    Search,
    TrackWhileScan,
    SingleTargetTrack,
    Standby,
    Silent
}

public class Launcher
{
    public string Id { get; init; } = "L1";
    public LauncherState State { get; set; } = LauncherState.Ready;
    public string MissileType { get; set; } = "9M38";
    public double ReloadTimeSec { get; set; } = 180.0;  // 3 min reload
    public double ReloadProgress { get; set; }          // 0-1
    public string? ActiveMissileId { get; set; }        // missile currently guided

    // Upgrade flags
    public bool HasRapidReload { get; set; }
    public bool HasAutoLoader { get; set; }

    public double EffectiveReloadTime => HasRapidReload
        ? ReloadTimeSec * 0.5
        : ReloadTimeSec;

    public void StartReload()
    {
        State = LauncherState.Reloading;
        ReloadProgress = 0;
    }

    public bool UpdateReload(double deltaTime)
    {
        if (State != LauncherState.Reloading) return false;
        ReloadProgress += deltaTime / EffectiveReloadTime;
        if (ReloadProgress >= 1.0)
        {
            ReloadProgress = 1.0;
            State = LauncherState.Ready;
            return true; // Reload complete
        }
        return false;
    }
}

/// <summary>
/// The player's SAM battery. Manages launchers, radar, ROE, and alert level.
/// Fixed position (origin) — cannot move unless Mobile Launcher upgrade purchased.
/// </summary>
public class SAMBattery : Entity
{
    // ── Identity ───────────────────────────────────────────────────────
    public string Callsign { get; set; } = "ALPHA";

    // ── Radar ──────────────────────────────────────────────────────────
    public RadarMode RadarMode { get; set; } = RadarMode.Search;
    public double RadarRangeNm { get; set; } = 80.0;
    public double RadarMinAltFt { get; set; } = 500.0; // Below this = terrain masked
    public bool RadarOnline { get; set; } = true;
    public bool HasLowAltitudeModule { get; set; }
    public bool HasECCMSuite { get; set; }
    public double RadarSweepAngle { get; set; }   // Current sweep angle (0-360)
    public double RadarSweepRateDegSec { get; set; } = 6.0; // 10-second rotation

    // ── Launchers ──────────────────────────────────────────────────────
    public List<Launcher> Launchers { get; set; } = new();
    public int MaxLaunchers { get; set; } = 4;

    // ── Missile inventory ──────────────────────────────────────────────
    public int ReserveMissiles { get; set; } = 12;
    public string CurrentWeaponId { get; set; } = WeaponCatalog.BaselineSarhWeaponId;
    public string CurrentMissileType { get; set; } = "9M38"; // Can be upgraded
    public List<string> AvailableWeaponIds { get; set; } = new() { WeaponCatalog.BaselineSarhWeaponId };
    public int MissilesFired { get; set; }
    public int ConfirmedKills { get; set; }
    public int Misses { get; set; }
    public double HitRate => MissilesFired == 0 ? 0 : (double)ConfirmedKills / MissilesFired;

    // ── Engagement state ───────────────────────────────────────────────
    public BatteryAlertLevel AlertLevel { get; set; } = BatteryAlertLevel.Yellow;
    public RulesOfEngagement ROE { get; set; } = RulesOfEngagement.WeaponsTight;
    public string? DesignatedTargetId { get; set; }  // Currently locked target

    // ── Systems health ─────────────────────────────────────────────────
    public bool PowerOnline { get; set; } = true;
    public bool CommsOnline { get; set; } = true;
    public bool CoolingNominal { get; set; } = true;
    public double RadarHealthPct { get; set; } = 1.0;
    public bool IsUnderAttack { get; set; }

    // ── Upgrade flags ──────────────────────────────────────────────────
    public bool HasDataLink { get; set; }
    public bool HasPassiveDetection { get; set; }
    public bool HasMobileCapability { get; set; }
    public bool HasDecoyEmitter { get; set; }
    public bool HasBackupPower { get; set; }
    public bool HasHardenedComms { get; set; }

    // ── Missile type specs (set when missile type is upgraded) ─────────
    public double MissileMaxRangeNm { get; set; } = 18.0;
    public double MissileMinRangeNm { get; set; } = 2.0;
    public double MissileMaxAltFt { get; set; } = 72000;
    public double MissileMinAltFt { get; set; } = 50;
    public double MissileSingleShotPk { get; set; } = 0.70;

    // ── Phase 4: Battery Network Coordination ──────────────────────────
    /// <summary>Unique identifier for the battery network (shared among coordinated batteries)</summary>
    public string? BatteryNetworkId { get; set; }

    /// <summary>True if this battery is the network coordinator</summary>
    public bool IsNetworkCoordinator { get; set; }

    /// <summary>List of linked battery IDs in the coordination network</summary>
    public List<string> LinkedBatteryIds { get; set; } = new();

    /// <summary>Track data shared with the network (trackId -> last update time)</summary>
    public Dictionary<string, DateTime> SharedTrackData { get; set; } = new();

    /// <summary>Current engagement assignment from coordinator</summary>
    public string? AssignedEngagementTrackId { get; set; }

    /// <summary>True if battery is in track-only mode (not engaging)</summary>
    public bool IsInTrackOnlyMode { get; set; }

    // ── Phase 4: SEAD Vulnerability (ARM Threats) ──────────────────────
    /// <summary>True if radar is actively emitting (vulnerable to ARM)</summary>
    public bool IsRadiating => RadarOnline && RadarMode is not RadarMode.Silent and not RadarMode.Standby;

    /// <summary>True if battery is being targeted by Anti-Radiation Missile</summary>
    public bool IsBeingTargetedByARM { get; private set; }

    /// <summary>Bearing of detected ARM threat (degrees)</summary>
    public double? ARMThreatBearing { get; private set; }

    /// <summary>Range of detected ARM threat (nautical miles)</summary>
    public double? ARMThreatRangeNm { get; private set; }

    /// <summary>Time when ARM threat was first detected</summary>
    public DateTime? ARMThreatDetectedTime { get; private set; }

    /// <summary>True if battery has gone silent to avoid ARM</summary>
    public bool IsInSilentMode { get; private set; }

    /// <summary>Time when battery entered silent mode</summary>
    public DateTime? SilentModeEnteredTime { get; private set; }

    /// <summary>Duration battery can remain in silent mode before needing to reactivate (seconds)</summary>
    public double SilentModeDurationSec { get; set; } = 120.0;

    public SAMBattery()
    {
        Type = EntityType.SAMBattery;
        Affiliation = Affiliation.Friendly;
        Position = Vec2.Zero; // Battery is at origin
        RcsM2 = 50.0; // Big radar = large RCS

        // Initialize default 4 launchers
        for (int i = 1; i <= 4; i++)
        {
            Launchers.Add(new Launcher
            {
                Id = $"L{i}",
                State = LauncherState.Ready,
                MissileType = "9M38"
            });
        }

        WeaponCatalog.EnsureBatteryWeapons(this);
    }

    public override void Update(double deltaTime)
    {
        // Battery doesn't move (unless mobile upgrade), but updates radar sweep and launcher states

        // Update SEAD/ARM threat state
        UpdateARMThreat(deltaTime);

        // Radar sweep rotation
        if (RadarOnline && RadarMode != RadarMode.Silent && RadarMode != RadarMode.Standby)
        {
            RadarSweepAngle = (RadarSweepAngle + RadarSweepRateDegSec * deltaTime) % 360.0;
        }

        // Update launcher reload timers
        foreach (var launcher in Launchers)
        {
            if (launcher.State == LauncherState.Reloading)
            {
                bool complete = launcher.UpdateReload(deltaTime);
                if (complete && ReserveMissiles > 0)
                {
                    ReserveMissiles--;
                }
                else if (complete && ReserveMissiles == 0)
                {
                    launcher.State = LauncherState.Empty;
                }
            }
        }
    }

    // ── Convenience methods ────────────────────────────────────────────

    public Launcher? GetReadyLauncher() =>
        Launchers.FirstOrDefault(l => l.State == LauncherState.Ready);

    public int ReadyLaunchers => Launchers.Count(l => l.State == LauncherState.Ready);
    public int ReloadingLaunchers => Launchers.Count(l => l.State == LauncherState.Reloading);

    public bool CanEngage(double targetRangeNm, double targetAltFt) =>
        targetRangeNm >= MissileMinRangeNm &&
        targetRangeNm <= MissileMaxRangeNm &&
        targetAltFt >= MissileMinAltFt &&
        targetAltFt <= MissileMaxAltFt &&
        ReadyLaunchers > 0;

    public double GetEffectiveRadarRange() =>
        RadarRangeNm * RadarHealthPct * (HasECCMSuite ? 1.0 : 1.0);

    public double GetEffectiveMinAltFt() =>
        HasLowAltitudeModule ? RadarMinAltFt * 0.4 : RadarMinAltFt;

    // ── Phase 4: SEAD Defense Methods ──────────────────────────────────

    /// <summary>
    /// Go silent to avoid Anti-Radiation Missile (ARM) threat.
    /// Turns off radar emissions to break ARM lock.
    /// </summary>
    public void GoSilent()
    {
        if (IsInSilentMode) return;

        RadarOnline = false;
        RadarMode = RadarMode.Silent;
        IsInSilentMode = true;
        SilentModeEnteredTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Reactivate radar after ARM threat has passed.
    /// </summary>
    public void GoActive()
    {
        if (!IsInSilentMode) return;

        RadarOnline = true;
        RadarMode = RadarMode.Search;
        IsInSilentMode = false;
        SilentModeEnteredTime = null;
        ClearARMThreat();
    }

    /// <summary>
    /// Detect and track ARM threat.
    /// Called when RWR detects ARM radar lock.
    /// </summary>
    public void DetectARMThreat(double bearingDeg, double rangeNm)
    {
        IsBeingTargetedByARM = true;
        ARMThreatBearing = bearingDeg;
        ARMThreatRangeNm = rangeNm;
        ARMThreatDetectedTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Clear ARM threat detection.
    /// </summary>
    public void ClearARMThreat()
    {
        IsBeingTargetedByARM = false;
        ARMThreatBearing = null;
        ARMThreatRangeNm = null;
        ARMThreatDetectedTime = null;
    }

    /// <summary>
    /// Update ARM threat state.
    /// Automatically go silent if ARM is inbound and radar is active.
    /// </summary>
    public void UpdateARMThreat(double deltaTime)
    {
        // Check if ARM threat has expired (no update for 30 seconds)
        if (ARMThreatDetectedTime.HasValue &&
            (DateTime.UtcNow - ARMThreatDetectedTime.Value).TotalSeconds > 30)
        {
            ClearARMThreat();
        }

        // Auto-evasion: Go silent if ARM is inbound and we're radiating
        if (IsBeingTargetedByARM && IsRadiating && !IsInSilentMode)
        {
            GoSilent();
        }

        // Check if we can come out of silent mode
        if (IsInSilentMode && SilentModeEnteredTime.HasValue)
        {
            double timeInSilentSec = (DateTime.UtcNow - SilentModeEnteredTime.Value).TotalSeconds;
            
            // Auto-reactivate after threat expires or timeout
            if (!IsBeingTargetedByARM || timeInSilentSec > SilentModeDurationSec)
            {
                GoActive();
            }
        }
    }
}
