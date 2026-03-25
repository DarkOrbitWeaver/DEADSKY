using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;

namespace DEADSKY.Core.Weapons;

public enum EngagementResult
{
    MissileInFlight,
    KillConfirmed,
    ProbableKill,
    MissDirect,         // Missed due to maneuver
    MissGuidanceLost,   // ECM or radar mode change broke guidance
    TargetLeftArea,
    AlreadyDestroyed
}

public record EngagementRecord(
    string TrackId,
    string EntityId,
    string MissileId,
    DateTime LaunchTime,
    EngagementResult Result,
    string Notes = "");

/// <summary>
/// Manages all weapon engagements. Handles the full cycle:
/// Designate → Launch → Guide → Assess.
/// </summary>
public class WeaponsSystem
{
    private readonly EntityManager _entities;
    private readonly TrackManager _tracks;

    public List<EngagementRecord> EngagementHistory { get; } = new();

    // Events
    public event Action<SAMMissile, Entity>? MissileLaunched;
    public event Action<SAMMissile, Entity?, EngagementResult>? EngagementCompleted;
    public event Action<string>? EngagementError; // Error message

    public WeaponsSystem(EntityManager entities, TrackManager tracks)
    {
        _entities = entities;
        _tracks = tracks;
    }

    // ── Main update ───────────────────────────────────────────────────

    /// <summary>Update all in-flight missiles. Call every simulation tick.</summary>
    public void Update(double deltaTime)
    {
        var missiles = _entities.GetActiveMissiles();
        var battery = _entities.GetPlayerBattery();

        foreach (var missile in missiles)
        {
            // Get target
            Entity? target = null;
            if (missile.TargetEntityId != null)
                target = _entities.Get(missile.TargetEntityId);

            // Guide missile
            if (target != null && target.IsActive && !missile.HasDetonated)
            {
                // SARH: requires radar to be in STT on this target
                if (missile.Guidance == GuidanceMode.SemiActiveRadar)
                {
                    bool radarIlluminating = battery?.DesignatedTargetId == target.Id;
                    if (!radarIlluminating)
                        missile.LoseGuidance();
                    else
                        missile.UpdateGuidance(target);
                }
                else
                {
                    missile.UpdateGuidance(target);
                }
            }
            else if (!missile.HasDetonated)
            {
                missile.LoseGuidance();
            }

            // Check detonation
            if (missile.HasDetonated)
            {
                ProcessDetonation(missile, target);
            }
        }
    }

    // ── Player commands ───────────────────────────────────────────────

    /// <summary>Designate (lock radar on) a track for engagement</summary>
    public bool DesignateTarget(string trackId)
    {
        var track = _tracks.GetById(trackId);
        if (track == null)
        {
            EngagementError?.Invoke($"Track {trackId} not found");
            return false;
        }

        _tracks.DesignateTrack(trackId);
        var battery = _entities.GetPlayerBattery();
        if (battery != null)
            battery.DesignatedTargetId = track.EntityId;

        return true;
    }

    /// <summary>Fire one missile at the designated target</summary>
    public EngagementResult FireAtDesignated(SAMBattery battery, string trackId)
    {
        var track = _tracks.GetById(trackId);
        if (track == null) { EngagementError?.Invoke("No designated target"); return EngagementResult.MissDirect; }

        Entity? target = track.EntityId != null ? _entities.Get(track.EntityId) : null;
        if (target == null || !target.IsActive)
        {
            EngagementError?.Invoke("Target no longer active");
            return EngagementResult.AlreadyDestroyed;
        }

        // Check engagement envelope
        double rangeNm = CoordinateSystem.MetersToNm(target.Position.Length);
        double altFt = CoordinateSystem.MToFt(target.AltitudeM);

        if (!battery.CanEngage(rangeNm, altFt))
        {
            string reason = rangeNm > battery.MissileMaxRangeNm ? "Target out of range" :
                           rangeNm < battery.MissileMinRangeNm ? "Target too close (min range)" :
                           "Target outside altitude envelope";
            EngagementError?.Invoke(reason);
            return EngagementResult.MissDirect;
        }

        // Get a ready launcher
        var launcher = battery.GetReadyLauncher();
        if (launcher == null)
        {
            EngagementError?.Invoke("No launchers ready — still reloading");
            return EngagementResult.MissDirect;
        }

        // Check ROE
        if (battery.ROE == RulesOfEngagement.WeaponsHold)
        {
            // Only allow if we're being directly attacked
            if (!battery.IsUnderAttack)
            {
                EngagementError?.Invoke("WEAPONS HOLD — cannot engage");
                return EngagementResult.MissDirect;
            }
        }
        if (battery.ROE == RulesOfEngagement.WeaponsTight)
        {
            if (track.Classification != TrackClassification.Hostile &&
                track.Classification != TrackClassification.AssumedHostile)
            {
                EngagementError?.Invoke("WEAPONS TIGHT — target not confirmed hostile");
                return EngagementResult.MissDirect;
            }
        }

        // LAUNCH
        var missile = _entities.LaunchMissile(battery, launcher, target,
            battery.CurrentMissileType, battery.MissileSingleShotPk);

        track.IsBeingEngaged = true;
        track.AssignedMissileId = missile.Id;
        target.IsBeingEngaged = true;
        target.EngagedByMissileId = missile.Id;

        MissileLaunched?.Invoke(missile, target);
        return EngagementResult.MissileInFlight;
    }

    /// <summary>Fire a salvo of N missiles at the designated target</summary>
    public List<EngagementResult> FireSalvo(SAMBattery battery, string trackId, int count)
    {
        var results = new List<EngagementResult>();
        for (int i = 0; i < count; i++)
        {
            var result = FireAtDesignated(battery, trackId);
            results.Add(result);
            if (result != EngagementResult.MissileInFlight) break;
        }
        return results;
    }

    // ── Detonation processing ─────────────────────────────────────────

    private void ProcessDetonation(SAMMissile missile, Entity? target)
    {
        var battery = _entities.GetPlayerBattery();
        EngagementResult result;

        if (missile.WasKill && target != null)
        {
            target.Status = EntityStatus.Destroyed;
            result = EngagementResult.KillConfirmed;
            if (battery != null) battery.ConfirmedKills++;

            // Drop the track — target is gone
            if (target != null)
                _tracks.DropTrackForEntity(target.Id);
        }
        else if (target != null && target.IsActive)
        {
            // Miss — target can evade
            if (target is Aircraft aircraft)
                aircraft.CurrentBehavior = AircraftBehavior.EvasiveManeuver;
            result = EngagementResult.MissDirect;
            if (battery != null) battery.Misses++;
        }
        else
        {
            result = EngagementResult.AlreadyDestroyed;
        }

        // Update engagement tracking
        if (target != null)
        {
            target.IsBeingEngaged = false;
            target.EngagedByMissileId = null;
        }

        // Update launcher state — launcher is already reloading (was set on launch)
        // Clear launcher's missile reference
        if (battery != null)
        {
            var launcher = battery.Launchers.FirstOrDefault(l => l.ActiveMissileId == missile.Id);
            if (launcher != null)
                launcher.ActiveMissileId = null;
        }

        var track = target != null ? _tracks.GetByEntityId(target.Id) : null;
        EngagementHistory.Add(new EngagementRecord(
            TrackId: track?.TrackId ?? "UNKNOWN",
            EntityId: target?.Id ?? "UNKNOWN",
            MissileId: missile.Id,
            LaunchTime: missile.SpawnTime,
            Result: result));

        EngagementCompleted?.Invoke(missile, target, result);

        // Mark missile as inactive
        missile.Status = EntityStatus.MissileSelfDestructed;
    }

    // ── Query ─────────────────────────────────────────────────────────

    public double CalculatePk(SAMBattery battery, TrackFile track)
    {
        // Base Pk from missile type
        double pk = battery.MissileSingleShotPk;

        // Modifiers
        // Range modifier: degrades at max and min range
        double maxRange = battery.MissileMaxRangeNm;
        double minRange = battery.MissileMinRangeNm;
        double rangeFactor = (track.RangeNm - minRange) / (maxRange - minRange);
        rangeFactor = Math.Clamp(rangeFactor, 0, 1);
        // Peak Pk at 40% of max range, degrades toward extremes
        double rangeModifier = 1.0 - Math.Abs(rangeFactor - 0.4) * 0.5;

        // ECM modifier
        var entity = track.EntityId != null ? _entities.Get(track.EntityId) : null;
        double ecmMod = 1.0;
        if (entity is Aircraft a && a.ECMActive && a.EcmPower > 0)
            ecmMod = 0.6;

        // Altitude modifier
        double altFt = track.AltitudeFt;
        double altMod = altFt < 500 ? 0.5 : 1.0; // Low altitude is harder

        return Math.Clamp(pk * rangeModifier * ecmMod * altMod, 0.05, 0.97);
    }

    public double CalculateSalvoPk(SAMBattery battery, TrackFile track, int salvoCount)
    {
        double singlePk = CalculatePk(battery, track);
        return 1.0 - Math.Pow(1.0 - singlePk, salvoCount);
    }
}
