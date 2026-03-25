using DEADSKY.Core.Physics;

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
    public string CurrentMissileType { get; set; } = "9M38"; // Can be upgraded
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
    }

    public override void Update(double deltaTime)
    {
        // Battery doesn't move (unless mobile upgrade), but updates radar sweep and launcher states

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
}
