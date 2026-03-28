using DEADSKY.Core.Entities;
using DEADSKY.Core.Logging;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Weapons;

public enum EngagementResult
{
    MissileInFlight,
    KillConfirmed,
    ProbableKill,
    MissDirect,
    MissGuidanceLost,
    TargetLeftArea,
    AlreadyDestroyed,
    AbortSuccessful
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
/// designate -> launch -> guide -> assess -> record consequences.
/// </summary>
public class WeaponsSystem
{
    private readonly EntityManager _entities;
    private readonly TrackManager _tracks;

    public List<EngagementRecord> EngagementHistory { get; } = new();
    public List<EngagementIncident> Incidents { get; } = new();
    public string LastError { get; private set; } = "";

    public event Action<SAMMissile, Entity>? MissileLaunched;
    public event Action<SAMMissile, Entity?, EngagementResult>? EngagementCompleted;
    public event Action<string>? EngagementError;
    public event Action<EngagementIncident>? IncidentRecorded;

    public WeaponsSystem(EntityManager entities, TrackManager tracks)
    {
        _entities = entities;
        _tracks = tracks;
    }

    public void Update(double deltaTime)
    {
        var missiles = _entities.GetByType<SAMMissile>()
            .Where(missile => !missile.DetonationProcessed)
            .ToList();
        var battery = _entities.GetPlayerBattery();

        UpdateThreatAwareness(battery, missiles);

        foreach (var missile in missiles)
        {
            Entity? target = string.IsNullOrWhiteSpace(missile.TargetEntityId)
                ? null
                : _entities.Get(missile.TargetEntityId);

            if (target != null && target.IsActive && !missile.HasDetonated)
            {
                if (missile.Guidance == GuidanceMode.SemiActiveRadar || missile.Guidance == GuidanceMode.CommandGuidance)
                {
                    bool supported = HasRadarSupportForTarget(battery, target);
                    if (supported)
                        missile.UpdateGuidance(target);
                    else
                        missile.UpdateGuidanceFromMemory(deltaTime);
                }
                else
                {
                    missile.UpdateGuidance(target);
                }
            }
            else if (!missile.HasDetonated)
            {
                missile.UpdateGuidanceFromMemory(deltaTime);
            }

            if (missile.HasDetonated)
            {
                ProcessDetonation(missile, target);
                missile.DetonationProcessed = true;
            }
        }
    }

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

    public EngagementResult FireAtDesignated(SAMBattery battery, string trackId)
    {
        var track = _tracks.GetById(trackId);
        if (track == null)
        {
            RaiseError("No designated target");
            return EngagementResult.MissDirect;
        }

        var weapon = WeaponCatalog.Get(battery.CurrentWeaponId);
        Entity? target = track.EntityId != null ? _entities.Get(track.EntityId) : null;
        if (target == null || !target.IsActive)
        {
            RaiseError("Target no longer active");
            return EngagementResult.AlreadyDestroyed;
        }

        if (track.Classification == TrackClassification.Friendly || target.Affiliation == Affiliation.Friendly)
        {
            RecordIncident(new EngagementIncident(
                "friendly_fire_attempt",
                $"Engagement denied on friendly track {track.TrackId}.",
                IncidentSeverity.Critical,
                DateTime.UtcNow,
                track.TrackId,
                target.Id,
                weapon.Id));
            RaiseError("FRIENDLY TRACK - cease fire");
            return EngagementResult.MissDirect;
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

        if (weapon.GuidanceMode == GuidanceMode.Infrared)
        {
            bool coldAspect = track.AspectString.Contains("COLD", StringComparison.OrdinalIgnoreCase);
            if (coldAspect && rangeNm > 4.5)
            {
                RaiseError("IR seeker poor on cold-aspect target");
                return EngagementResult.MissDirect;
            }
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

        if (battery.ROE == RulesOfEngagement.WeaponsTight &&
            track.Classification is not (TrackClassification.Hostile or TrackClassification.AssumedHostile))
        {
            RaiseError("WEAPONS TIGHT - target not confirmed hostile");
            return EngagementResult.MissDirect;
        }

        if (weapon.RequiresRadarSupport && !HasRadarSupportForTarget(battery, target))
        {
            RaiseError("Weapon requires radar support");
            return EngagementResult.MissDirect;
        }

        var missile = _entities.LaunchMissile(
            battery,
            launcher,
            target,
            weapon.ShortCode,
            weapon.BaseSingleShotPk);

        track.IsBeingEngaged = true;
        track.AssignedMissileId = missile.Id;
        target.IsBeingEngaged = true;
        target.EngagedByMissileId = missile.Id;

        MissileLaunched?.Invoke(missile, target);
        return EngagementResult.MissileInFlight;
    }

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

    public bool CanAbortTrack(string trackId)
    {
        var track = _tracks.GetById(trackId);
        if (track?.AssignedMissileId == null)
            return false;

        return _entities.Get(track.AssignedMissileId) is SAMMissile missile && missile.CanAbortInFlight && !missile.HasDetonated;
    }

    public int AbortTrackEngagement(string trackId)
    {
        var track = _tracks.GetById(trackId);
        if (track?.AssignedMissileId == null)
            return 0;

        if (_entities.Get(track.AssignedMissileId) is not SAMMissile missile || !missile.TryAbort())
            return 0;

        RecordIncident(new EngagementIncident(
            "engagement_abort",
            $"Abort ordered for missile {missile.MissileTypeName} against {track.TrackId}.",
            IncidentSeverity.Warning,
            DateTime.UtcNow,
            track.TrackId,
            track.EntityId,
            missile.WeaponId));

        return 1;
    }

    public void RecordOperationalIncident(
        string incidentType,
        string summary,
        IncidentSeverity severity,
        string? trackId = null,
        string? entityId = null,
        string? weaponId = null)
    {
        RecordIncident(new EngagementIncident(
            incidentType,
            summary,
            severity,
            DateTime.UtcNow,
            trackId,
            entityId,
            weaponId));
    }

    public double CalculatePk(SAMBattery battery, TrackFile track)
    {
        var weapon = WeaponCatalog.Get(battery.CurrentWeaponId);
        double pk = weapon.BaseSingleShotPk;

        double maxRange = battery.MissileMaxRangeNm;
        double minRange = battery.MissileMinRangeNm;
        double rangeFactor = (track.RangeNm - minRange) / Math.Max(0.1, maxRange - minRange);
        rangeFactor = Math.Clamp(rangeFactor, 0, 1);
        double rangeModifier = 1.0 - Math.Abs(rangeFactor - 0.4) * 0.5;

        var entity = track.EntityId != null ? _entities.Get(track.EntityId) : null;
        double countermeasureMod = 1.0;
        if (entity is Aircraft aircraft)
        {
            if (weapon.SusceptibleToChaff && (aircraft.ECMActive || aircraft.IsChaffActive))
                countermeasureMod *= 0.68;
            if (weapon.SusceptibleToFlares && aircraft.IsFlareActive)
                countermeasureMod *= 0.62;
        }

        double altMod = track.AltitudeFt < 500 ? 0.5 : 1.0;

        return Math.Clamp(pk * rangeModifier * countermeasureMod * altMod, 0.05, 0.97);
    }

    public double CalculateSalvoPk(SAMBattery battery, TrackFile track, int salvoCount)
    {
        double singlePk = CalculatePk(battery, track);
        return 1.0 - Math.Pow(1.0 - singlePk, salvoCount);
    }

    private void UpdateThreatAwareness(SAMBattery? battery, IReadOnlyList<SAMMissile> missiles)
    {
        DateTime now = DateTime.UtcNow;
        bool radarHot = battery?.RadarOnline == true &&
                        battery.RadarMode is RadarMode.Search or RadarMode.TrackWhileScan or RadarMode.SingleTargetTrack;

        if (radarHot && battery != null)
        {
            foreach (var hostile in _entities.GetHostileAircraft().OfType<Aircraft>())
            {
                hostile.RadarLockDetected = true;
                hostile.RadarLockDetectedTime = now;
                hostile.RadarLockBearingDeg = hostile.Position.HeadingTo(battery.Position);

                bool hardLock = battery.RadarMode == RadarMode.SingleTargetTrack &&
                                battery.DesignatedTargetId == hostile.Id;
                hostile.HardLockDetected = hardLock;
                hostile.HardLockDetectedTime = hardLock ? now : hostile.HardLockDetectedTime;
            }
        }
        else
        {
            foreach (var hostile in _entities.GetHostileAircraft().OfType<Aircraft>())
                hostile.HardLockDetected = false;
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

        CoordinatePackageResponses(battery);
    }

    private void CoordinatePackageResponses(SAMBattery? battery)
    {
        if (battery == null)
            return;

        var hostiles = _entities.GetHostileAircraft().OfType<Aircraft>().ToList();
        bool innerRingPressure = hostiles.Any(aircraft => aircraft.Position.Length <= CoordinateSystem.NmToMeters(24));
        bool radarEmitting = battery.RadarOnline && battery.RadarMode is RadarMode.TrackWhileScan or RadarMode.SingleTargetTrack or RadarMode.Search;

        foreach (var hostile in hostiles)
        {
            if (hostile.Role == AircraftRole.SEAD && hostile.HasARMCapability && radarEmitting)
            {
                if (hostile.CurrentBehavior != AircraftBehavior.SEAD)
                    GameSessionLogger.Current?.OnBehaviorChanged(hostile.Id, hostile.Designation, hostile.CurrentBehavior.ToString(), nameof(AircraftBehavior.SEAD), "sead_arm_radar_emitting");
                hostile.CurrentBehavior = AircraftBehavior.SEAD;
                continue;
            }

            if (hostile.Role == AircraftRole.ECMEscort && (hostile.RadarLockDetected || innerRingPressure))
            {
                if (hostile.CurrentBehavior != AircraftBehavior.ECMStandoff)
                    GameSessionLogger.Current?.OnBehaviorChanged(hostile.Id, hostile.Designation, hostile.CurrentBehavior.ToString(), nameof(AircraftBehavior.ECMStandoff), innerRingPressure ? "inner_ring" : "radar_lock");
                hostile.CurrentBehavior = AircraftBehavior.ECMStandoff;
                continue;
            }

            if (hostile.Role == AircraftRole.Fighter && (hostile.HardLockDetected || innerRingPressure))
            {
                if (hostile.CurrentBehavior != AircraftBehavior.EscortCover)
                    GameSessionLogger.Current?.OnBehaviorChanged(hostile.Id, hostile.Designation, hostile.CurrentBehavior.ToString(), nameof(AircraftBehavior.EscortCover), innerRingPressure ? "inner_ring" : "hard_lock");
                hostile.CurrentBehavior = AircraftBehavior.EscortCover;
                continue;
            }

            if (hostile.Role == AircraftRole.Striker && hostile.HardLockDetected)
            {
                if (hostile.CurrentBehavior != AircraftBehavior.TerrainFollowing)
                    GameSessionLogger.Current?.OnBehaviorChanged(hostile.Id, hostile.Designation, hostile.CurrentBehavior.ToString(), nameof(AircraftBehavior.TerrainFollowing), "hard_lock");
                hostile.CurrentBehavior = AircraftBehavior.TerrainFollowing;
            }
        }
    }

    private bool HasRadarSupportForTarget(SAMBattery? battery, Entity target)
    {
        if (battery?.RadarOnline != true)
            return false;

        var guidanceTrack = _tracks.GetByEntityId(target.Id);
        bool radarIlluminating = battery.RadarMode == RadarMode.SingleTargetTrack &&
                                 battery.DesignatedTargetId == target.Id;
        bool twsSupport = battery.RadarMode == RadarMode.TrackWhileScan &&
                          guidanceTrack != null &&
                          guidanceTrack.IsTrackHeld &&
                          guidanceTrack.Quality != TrackQuality.Lost;
        return radarIlluminating || twsSupport;
    }

    private void ProcessDetonation(SAMMissile missile, Entity? target)
    {
        var battery = _entities.GetPlayerBattery();
        var preImpactTrack = target != null ? _tracks.GetByEntityId(target.Id) : null;
        EngagementResult result;

        if (missile.AbortRequested)
        {
            result = EngagementResult.AbortSuccessful;
        }
        else if (missile.CountermeasureDecoyed)
        {
            result = EngagementResult.MissGuidanceLost;
        }
        else if (missile.WasKill && target != null)
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
            Result: result,
            Notes: missile.CountermeasureDecoyed ? "Countermeasure decoyed." : missile.AbortRequested ? "Abort commanded." : ""));

        if (result == EngagementResult.MissGuidanceLost)
        {
            RecordIncident(new EngagementIncident(
                "guidance_break",
                $"Missile {missile.MissileTypeName} lost guidance on {preImpactTrack?.TrackId ?? "UNKNOWN"} after countermeasures.",
                IncidentSeverity.Warning,
                DateTime.UtcNow,
                preImpactTrack?.TrackId,
                target?.Id,
                missile.WeaponId));
        }

        EngagementCompleted?.Invoke(missile, target, result);

        if (result == EngagementResult.KillConfirmed && target != null)
            _tracks.DropTrackForEntity(target.Id);

        missile.Status = EntityStatus.MissileSelfDestructed;
    }

    private void RecordIncident(EngagementIncident incident)
    {
        Incidents.Add(incident);
        while (Incidents.Count > 40)
            Incidents.RemoveAt(0);
        IncidentRecorded?.Invoke(incident);
    }

    private void RaiseError(string message)
    {
        LastError = message;
        EngagementError?.Invoke(message);
    }
}
