using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;

namespace DEADSKY.Core.Weapons;

public enum EngagementResult
{
    MissileInFlight,
    KillConfirmed,
    ProbableKill,
    MissDirect,
    MissGuidanceLost,
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
/// Designate -> Launch -> Guide -> Assess.
/// </summary>
public class WeaponsSystem
{
    private readonly EntityManager _entities;
    private readonly TrackManager _tracks;

    public List<EngagementRecord> EngagementHistory { get; } = new();
    public string LastError { get; private set; } = "";

    public event Action<SAMMissile, Entity>? MissileLaunched;
    public event Action<SAMMissile, Entity?, EngagementResult>? EngagementCompleted;
    public event Action<string>? EngagementError;

    public WeaponsSystem(EntityManager entities, TrackManager tracks)
    {
        _entities = entities;
        _tracks = tracks;
    }

    /// <summary>Update all in-flight missiles. Call every simulation tick.</summary>
    public void Update(double deltaTime)
    {
        var missiles = _entities.GetByType<SAMMissile>()
            .Where(missile => !missile.DetonationProcessed)
            .ToList();
        var battery = _entities.GetPlayerBattery();

        UpdateThreatAwareness(battery, missiles);

        foreach (var missile in missiles)
        {
            Entity? target = null;
            if (missile.TargetEntityId != null)
                target = _entities.Get(missile.TargetEntityId);

            if (target != null && target.IsActive && !missile.HasDetonated)
            {
                if (missile.Guidance == GuidanceMode.SemiActiveRadar)
                {
                    bool radarIlluminating = battery?.RadarOnline == true
                        && battery.RadarMode == RadarMode.SingleTargetTrack
                        && battery.DesignatedTargetId == target.Id;

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

            if (missile.HasDetonated)
            {
                ProcessDetonation(missile, target);
                missile.DetonationProcessed = true;
            }
        }
    }

    /// <summary>Designate (lock radar on) a track for engagement</summary>
    public bool DesignateTarget(string trackId)
    {
        var track = _tracks.GetById(trackId);
        if (track == null)
        {
            RaiseError($"Track {trackId} not found");
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
        if (track == null)
        {
            RaiseError("No designated target");
            return EngagementResult.MissDirect;
        }

        Entity? target = track.EntityId != null ? _entities.Get(track.EntityId) : null;
        if (target == null || !target.IsActive)
        {
            RaiseError("Target no longer active");
            return EngagementResult.AlreadyDestroyed;
        }

        double rangeNm = CoordinateSystem.MetersToNm(target.Position.Length);
        double altFt = CoordinateSystem.MToFt(target.AltitudeM);

        if (!battery.CanEngage(rangeNm, altFt))
        {
            string reason = rangeNm > battery.MissileMaxRangeNm ? "Target out of range" :
                           rangeNm < battery.MissileMinRangeNm ? "Target too close (min range)" :
                           "Target outside altitude envelope";
            RaiseError(reason);
            return EngagementResult.MissDirect;
        }

        var launcher = battery.GetReadyLauncher();
        if (launcher == null)
        {
            RaiseError("No launchers ready - still reloading");
            return EngagementResult.MissDirect;
        }

        if (battery.ROE == RulesOfEngagement.WeaponsHold && !battery.IsUnderAttack)
        {
            RaiseError("WEAPONS HOLD - cannot engage");
            return EngagementResult.MissDirect;
        }

        if (battery.ROE == RulesOfEngagement.WeaponsTight
            && track.Classification is not (TrackClassification.Hostile or TrackClassification.AssumedHostile))
        {
            RaiseError("WEAPONS TIGHT - target not confirmed hostile");
            return EngagementResult.MissDirect;
        }

        var missile = _entities.LaunchMissile(
            battery,
            launcher,
            target,
            battery.CurrentMissileType,
            battery.MissileSingleShotPk);

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
            if (result != EngagementResult.MissileInFlight)
                break;
        }

        return results;
    }

    private void UpdateThreatAwareness(SAMBattery? battery, IReadOnlyList<SAMMissile> missiles)
    {
        DateTime now = DateTime.UtcNow;

        if (battery?.RadarMode == RadarMode.SingleTargetTrack &&
            !string.IsNullOrWhiteSpace(battery.DesignatedTargetId) &&
            _entities.Get(battery.DesignatedTargetId) is Aircraft lockedAircraft)
        {
            lockedAircraft.RadarLockDetected = true;
            lockedAircraft.RadarLockDetectedTime = now;
            lockedAircraft.RadarLockBearingDeg = lockedAircraft.Position.HeadingTo(battery.Position);
        }

        foreach (var missile in missiles)
        {
            if (!missile.IsActive || missile.HasDetonated || string.IsNullOrWhiteSpace(missile.TargetEntityId))
                continue;

            if (_entities.Get(missile.TargetEntityId) is not Aircraft threatenedAircraft)
                continue;

            threatenedAircraft.MissileInbound = true;
            threatenedAircraft.MissileInboundDetectedTime = now;
        }
    }

    private void ProcessDetonation(SAMMissile missile, Entity? target)
    {
        var battery = _entities.GetPlayerBattery();
        var preImpactTrack = target != null ? _tracks.GetByEntityId(target.Id) : null;
        EngagementResult result;

        if (missile.WasKill && target != null)
        {
            target.Status = EntityStatus.Destroyed;
            result = EngagementResult.KillConfirmed;
            if (battery != null)
                battery.ConfirmedKills++;
        }
        else if (target != null && target.IsActive)
        {
            if (target is Aircraft aircraft)
                aircraft.CurrentBehavior = AircraftBehavior.EvasiveManeuver;

            result = EngagementResult.MissDirect;
            if (battery != null)
                battery.Misses++;
        }
        else
        {
            result = EngagementResult.AlreadyDestroyed;
        }

        if (target != null)
        {
            target.IsBeingEngaged = false;
            target.EngagedByMissileId = null;

            if (target is Aircraft aircraft)
                aircraft.MissileInbound = false;
        }

        if (preImpactTrack != null)
        {
            preImpactTrack.IsBeingEngaged = false;
            preImpactTrack.AssignedMissileId = null;
        }

        if (battery != null)
        {
            var launcher = battery.Launchers.FirstOrDefault(l => l.ActiveMissileId == missile.Id);
            if (launcher != null)
                launcher.ActiveMissileId = null;
        }

        EngagementHistory.Add(new EngagementRecord(
            TrackId: preImpactTrack?.TrackId ?? "UNKNOWN",
            EntityId: target?.Id ?? "UNKNOWN",
            MissileId: missile.Id,
            LaunchTime: missile.SpawnTime,
            Result: result));

        EngagementCompleted?.Invoke(missile, target, result);

        if (result == EngagementResult.KillConfirmed && target != null)
            _tracks.DropTrackForEntity(target.Id);

        missile.Status = EntityStatus.MissileSelfDestructed;
    }

    public double CalculatePk(SAMBattery battery, TrackFile track)
    {
        double pk = battery.MissileSingleShotPk;

        double maxRange = battery.MissileMaxRangeNm;
        double minRange = battery.MissileMinRangeNm;
        double rangeFactor = (track.RangeNm - minRange) / (maxRange - minRange);
        rangeFactor = Math.Clamp(rangeFactor, 0, 1);
        double rangeModifier = 1.0 - Math.Abs(rangeFactor - 0.4) * 0.5;

        var entity = track.EntityId != null ? _entities.Get(track.EntityId) : null;
        double ecmMod = 1.0;
        if (entity is Aircraft a && a.ECMActive && a.EcmPower > 0)
            ecmMod = 0.6;

        double altFt = track.AltitudeFt;
        double altMod = altFt < 500 ? 0.5 : 1.0;

        return Math.Clamp(pk * rangeModifier * ecmMod * altMod, 0.05, 0.97);
    }

    public double CalculateSalvoPk(SAMBattery battery, TrackFile track, int salvoCount)
    {
        double singlePk = CalculatePk(battery, track);
        return 1.0 - Math.Pow(1.0 - singlePk, salvoCount);
    }

    private void RaiseError(string message)
    {
        LastError = message;
        EngagementError?.Invoke(message);
    }
}
