using DEADSKY.Core.Logging;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Comms;

namespace DEADSKY.Core.Entities;

public enum AircraftBehavior
{
    IngressAttack,       // Flying toward target
    EgressRetreat,       // Running away
    OrbitPatrol,         // Racetrack/orbit pattern
    DefensiveBeam,       // Beaming/notching radar threat
    EscortCover,         // Fighters pushing forward to cover a striker package
    EvasiveManeuver,     // Random jinking under fire
    TerrainFollowing,    // Hugging the ground
    PopUpAttack,         // Low-level approach then pop-up
    Feint,               // Fake approach to draw fire
    ECMStandoff,         // Jamming from distance
    FormationLead,       // Leading a formation
    FormationWingman,    // Following a leader
    Loiter,              // Waiting for orders
    SEAD,                // Searching for radar emissions to destroy
    BDA                  // Battle Damage Assessment
}

public enum AircraftRole
{
    Striker,
    Fighter,
    ECMEscort,
    SEAD,
    Recon,
    Tanker,
    Decoy
}

public enum WeaponType
{
    None,
    Aim120,  // AIM-120 AMRAAM (active radar, BVR)
    Aim9     // AIM-9 Sidewinder (IR, WVR)
}

/// <summary>
/// A fixed-wing or rotary aircraft. Handles waypoint navigation,
/// behavior state, fuel, ECM, and RWR.
/// </summary>
public class Aircraft : Entity
{
    // ── Configuration ─────────────────────────────────────────────────
    public AircraftRole Role { get; init; } = AircraftRole.Fighter;
    public double FuelCapacityKg { get; init; } = 5000.0;
    public double FuelBurnRateKgSec { get; init; } = 2.5;
    public double BingoFuelKg { get; init; } = 800.0;
    public bool HasECM { get; init; }
    public bool HasARMCapability { get; init; }     // Anti-radiation missiles
    public double EcmPower { get; init; } = 0.0;    // watts ERP if ECM equipped
    public double AggressivenessLevel { get; set; } = 0.7; // 0-1, affects AI decisions

    // ── State ──────────────────────────────────────────────────────────
    public AircraftBehavior CurrentBehavior { get; set; } = AircraftBehavior.IngressAttack;
    public double FuelRemainingKg { get; set; }
    public bool IsOnRTB => FuelRemainingKg <= BingoFuelKg;
    public bool HasTarget => TargetEntityId != null;

    // ── Mission state ─────────────────────────────────────────────────
    public string? TargetEntityId { get; set; }  // What it's trying to hit/intercept
    public Vec2? TargetWaypoint { get; set; }     // Current navigation target
    public List<Vec2> Waypoints { get; set; } = new();
    public int CurrentWaypointIndex { get; set; }

    // ── Group/Formation ───────────────────────────────────────────────
    public string? GroupId { get; set; }
    public string? FormationLeaderId { get; set; }
    public int FormationSlot { get; set; }         // 0=lead, 1-3=wingmen
    public string? PackageRoleLabel { get; set; }
    public string? MissionObjectiveId { get; set; }
    public string? MissionObjectiveName { get; set; }
    public string? EntryLabel { get; set; }
    public Vec2? ObjectivePosition { get; set; }

    // ── Friendly fighter support (CAP) ────────────────────────────────
    public int Aim120Count { get; set; }           // AIM-120 AMRAAM (active radar)
    public int Aim9Count { get; set; }             // AIM-9 Sidewinder (IR)
    public bool IsWinchester => Aim120Count == 0 && Aim9Count == 0;
    public string? PatrolSectorId { get; set; }    // Assigned patrol sector
    public Vec2? PatrolSectorCenter { get; set; }  // Center point of patrol sector
    public double PatrolSectorRadiusM { get; set; } // Patrol sector radius in meters
    public string? InterceptTargetTrackId { get; set; } // Assigned intercept target
    public bool IsOnStation { get; set; }          // Has arrived at patrol sector
    public DateTime? OnStationTime { get; set; }   // When aircraft arrived on station
    
    // ── Radio communications ──────────────────────────────────────────
    public CommManager? CommManager { get; set; }  // Radio communication manager
    private bool _hasReportedOnStation;            // Track if on-station report sent
    private string? _lastTallyTargetId;            // Track last target for which tally was reported
    
    // ── Weapon engagement constants ───────────────────────────────────
    private const double Aim120MaxRangeNm = 30.0;  // AIM-120 max range ~30nm
    private const double Aim120MinRangeNm = 3.0;   // AIM-120 min range ~3nm
    private const double Aim9MaxRangeNm = 10.0;    // AIM-9 max range ~10nm
    private const double Aim9MinRangeNm = 0.5;     // AIM-9 min range ~0.5nm
    private const double BvrRangeThresholdNm = 12.0; // BVR/WVR transition ~12nm

    // ── Threat awareness (what the aircraft "knows") ──────────────────
    public bool RadarLockDetected { get; set; }    // RWR is screaming
    public double RadarLockBearingDeg { get; set; }
    public DateTime? RadarLockDetectedTime { get; set; }
    public bool HardLockDetected { get; set; }
    public DateTime? HardLockDetectedTime { get; set; }
    public bool MissileInbound { get; set; }
    public DateTime? MissileInboundDetectedTime { get; set; }
    public int ChaffCharges { get; set; } = 4;
    public int FlareCharges { get; set; } = 4;
    public DateTime? ChaffActiveUntilUtc { get; private set; }
    public DateTime? FlareActiveUntilUtc { get; private set; }
    public bool IsChaffActive => ChaffActiveUntilUtc.HasValue && ChaffActiveUntilUtc.Value > DateTime.UtcNow;
    public bool IsFlareActive => FlareActiveUntilUtc.HasValue && FlareActiveUntilUtc.Value > DateTime.UtcNow;

    // ── Evasion state ─────────────────────────────────────────────────
    private double _evasionTimer;
    private double _evasionTargetHeading;
    private double _evasionTargetAlt;
    private AircraftBehavior? _behaviorBeforeThreatReaction;
    private DateTime? _threatReactionUntilUtc;

    public Aircraft()
    {
        Type = EntityType.Aircraft;
        FuelRemainingKg = FuelCapacityKg;
    }

    public override void Update(double deltaTime)
    {
        if (!IsActive) return;

        // Burn fuel
        FuelRemainingKg -= FuelBurnRateKgSec * deltaTime;
        FuelRemainingKg = Math.Max(0, FuelRemainingKg);

        if (IsOnRTB && CurrentBehavior != AircraftBehavior.EgressRetreat)
        {
            GameSessionLogger.Current?.OnBehaviorChanged(Id, Designation, CurrentBehavior.ToString(), nameof(AircraftBehavior.EgressRetreat), "bingo_fuel");
            CurrentBehavior = AircraftBehavior.EgressRetreat;
            // Point away from battery
            RequestedHeadingDeg = (HeadingDeg + 180.0) % 360.0;
        }

        RefreshThreatReactionState();

        // Execute behavior
        switch (CurrentBehavior)
        {
            case AircraftBehavior.IngressAttack:
                ExecuteIngress(deltaTime);
                
                // Strikers "attack" when reaching objective
                if (Role == AircraftRole.Striker && ObjectivePosition.HasValue)
                {
                    double rangeToObjective = Position.DistanceTo(ObjectivePosition.Value);
                    if (rangeToObjective < CoordinateSystem.NmToMeters(3))
                    {
                        GameSessionLogger.Current?.OnBehaviorChanged(Id, Designation, nameof(AircraftBehavior.IngressAttack), nameof(AircraftBehavior.EgressRetreat), "objective_reached");
                        CurrentBehavior = AircraftBehavior.EgressRetreat;
                        RequestedHeadingDeg = (HeadingDeg + 180.0) % 360.0;
                    }
                }
                break;

            case AircraftBehavior.EgressRetreat:
                ExecuteEgress(deltaTime);
                break;

            case AircraftBehavior.EvasiveManeuver:
                ExecuteEvasion(deltaTime);
                break;

            case AircraftBehavior.DefensiveBeam:
                ExecuteDefensiveBeam();
                break;

            case AircraftBehavior.TerrainFollowing:
                RequestedAltitudeM = CoordinateSystem.FtToM(200); // 200ft AGL
                ExecuteIngress(deltaTime);
                break;

            case AircraftBehavior.PopUpAttack:
                ExecutePopUp(deltaTime);
                break;

            case AircraftBehavior.OrbitPatrol:
                ExecuteOrbit(deltaTime);
                break;

            case AircraftBehavior.ECMStandoff:
                ExecuteEcmStandoff();
                break;

            case AircraftBehavior.EscortCover:
                ExecuteEscortCover();
                break;

            case AircraftBehavior.SEAD:
                ExecuteSead();
                break;

            case AircraftBehavior.Feint:
                // Act like IngressAttack but stop at a certain range
                double range = Position.Length;
                if (range < CoordinateSystem.NmToMeters(25))
                {
                    GameSessionLogger.Current?.OnBehaviorChanged(Id, Designation, nameof(AircraftBehavior.Feint), nameof(AircraftBehavior.EgressRetreat), "feint_range");
                    CurrentBehavior = AircraftBehavior.EgressRetreat;
                    RequestedHeadingDeg = (HeadingDeg + 180.0) % 360.0;
                }
                else
                    ExecuteIngress(deltaTime);
                break;
        }

        base.Update(deltaTime);
    }

    private void RefreshThreatReactionState()
    {
        DateTime now = DateTime.UtcNow;
        bool radarSpikeHot = RadarLockDetected &&
                             RadarLockDetectedTime.HasValue &&
                             now - RadarLockDetectedTime.Value <= TimeSpan.FromSeconds(4);
        bool hardLockHot = HardLockDetected &&
                           HardLockDetectedTime.HasValue &&
                           now - HardLockDetectedTime.Value <= TimeSpan.FromSeconds(5);
        bool missileThreatHot = MissileInbound &&
                                MissileInboundDetectedTime.HasValue &&
                                now - MissileInboundDetectedTime.Value <= TimeSpan.FromSeconds(8);

        ECMActive = HasECM && (radarSpikeHot || hardLockHot || missileThreatHot);

        if ((radarSpikeHot || hardLockHot) && ChaffCharges > 0)
            DeployChaff();

        if (missileThreatHot && FlareCharges > 0)
            DeployFlares();

        if (missileThreatHot)
        {
            if (CurrentBehavior != AircraftBehavior.EvasiveManeuver)
            {
                _behaviorBeforeThreatReaction = CurrentBehavior;
                GameSessionLogger.Current?.OnBehaviorChanged(Id, Designation, CurrentBehavior.ToString(), nameof(AircraftBehavior.EvasiveManeuver), "missile_inbound");
                CurrentBehavior = AircraftBehavior.EvasiveManeuver;
            }
            
            _threatReactionUntilUtc = now.AddSeconds(5);
            return;
        }

        if ((hardLockHot || radarSpikeHot) && CurrentBehavior != AircraftBehavior.EvasiveManeuver)
        {
            if (!IsThreatResponseBehavior(CurrentBehavior))
                _behaviorBeforeThreatReaction = CurrentBehavior;

            var next = ChooseRadarThreatBehavior();
            
            // ONLY log if behavior actually changes
            if (CurrentBehavior != next)
            {
                GameSessionLogger.Current?.OnBehaviorChanged(Id, Designation, CurrentBehavior.ToString(), next.ToString(), hardLockHot ? "hard_lock" : "radar_lock");
                CurrentBehavior = next;
            }
            
            _threatReactionUntilUtc = now.AddSeconds(hardLockHot ? 7 : 5);
            return;
        }

        if (CurrentBehavior == AircraftBehavior.EvasiveManeuver &&
            _behaviorBeforeThreatReaction.HasValue &&
            _threatReactionUntilUtc.HasValue &&
            now >= _threatReactionUntilUtc.Value)
        {
            GameSessionLogger.Current?.OnBehaviorChanged(Id, Designation, CurrentBehavior.ToString(), _behaviorBeforeThreatReaction.Value.ToString(), "threat_clear");
            CurrentBehavior = _behaviorBeforeThreatReaction.Value;
            _behaviorBeforeThreatReaction = null;
            _threatReactionUntilUtc = null;
        }
        else if ((CurrentBehavior == AircraftBehavior.DefensiveBeam ||
                  CurrentBehavior == AircraftBehavior.ECMStandoff ||
                  CurrentBehavior == AircraftBehavior.EscortCover ||
                  CurrentBehavior == AircraftBehavior.SEAD ||
                  CurrentBehavior == AircraftBehavior.TerrainFollowing) &&
                 _behaviorBeforeThreatReaction.HasValue &&
                 _threatReactionUntilUtc.HasValue &&
                 now >= _threatReactionUntilUtc.Value)
        {
            GameSessionLogger.Current?.OnBehaviorChanged(Id, Designation, CurrentBehavior.ToString(), _behaviorBeforeThreatReaction.Value.ToString(), "threat_clear");
            CurrentBehavior = _behaviorBeforeThreatReaction.Value;
            _behaviorBeforeThreatReaction = null;
            _threatReactionUntilUtc = null;
        }

        if (!radarSpikeHot)
            RadarLockDetected = false;
        if (!hardLockHot)
            HardLockDetected = false;

        if (!missileThreatHot)
            MissileInbound = false;
    }

    private void ExecuteIngress(double deltaTime)
    {
        if (TargetWaypoint.HasValue)
        {
            // Steer toward current waypoint
            double bearing = Position.HeadingTo(TargetWaypoint.Value);
            RequestedHeadingDeg = bearing;

            // Check if we've reached the waypoint (within 500m)
            if (Position.DistanceTo(TargetWaypoint.Value) < 500)
            {
                if (CurrentWaypointIndex < Waypoints.Count - 1)
                {
                    CurrentWaypointIndex++;
                    TargetWaypoint = Waypoints[CurrentWaypointIndex];
                }
            }
        }
        else
        {
            // No waypoint — fly toward origin (battery position)
            RequestedHeadingDeg = Position.HeadingTo(Vec2.Zero);
        }
    }

    private void ExecuteEgress(double deltaTime)
    {
        // Fly away — heading should already be set away from threat
        // Climb for speed
        RequestedAltitudeM = CoordinateSystem.FtToM(25000);
        RequestedSpeedMps = FlightModel.MaxSpeedMps;
    }

    private void ExecuteEvasion(double deltaTime)
    {
        _evasionTimer -= deltaTime;
        if (_evasionTimer <= 0)
        {
            // Pick new random evasion heading (jinking)
            var rng = SimulationRandom.Instance;
            _evasionTargetHeading = (HeadingDeg + rng.NextDouble() * 120 - 60 + 360) % 360;
            _evasionTargetAlt = AltitudeM + (rng.NextDouble() * 3000 - 1500);
            _evasionTargetAlt = Math.Clamp(_evasionTargetAlt,
                CoordinateSystem.FtToM(500), CoordinateSystem.FtToM(40000));
            _evasionTimer = 3.0 + rng.NextDouble() * 4.0; // 3-7 seconds
        }
        RequestedHeadingDeg = _evasionTargetHeading;
        RequestedAltitudeM = _evasionTargetAlt;
        RequestedSpeedMps = FlightModel.MaxSpeedMps; // Full afterburner
    }

    private void ExecuteDefensiveBeam()
    {
        double beamOffset = AggressivenessLevel >= 0.65 ? 95 : 85;
        RequestedHeadingDeg = NormalizeHeading(RadarLockBearingDeg + beamOffset);
        RequestedAltitudeM = Math.Clamp(
            AltitudeM + CoordinateSystem.FtToM(AggressivenessLevel >= 0.6 ? 1200 : -900),
            CoordinateSystem.FtToM(800),
            CoordinateSystem.FtToM(38000));
        RequestedSpeedMps = FlightModel.MaxSpeedMps * 0.92;
    }

    private void ExecutePopUp(double deltaTime)
    {
        double rangeM = Position.DistanceTo(ResolveMissionAnchor());
        double popUpRangeM = CoordinateSystem.NmToMeters(15);

        if (rangeM > popUpRangeM)
        {
            // Low-level approach
            RequestedAltitudeM = CoordinateSystem.FtToM(200);
            ExecuteIngress(deltaTime);
        }
        else
        {
            // Pop up to attack altitude
            RequestedAltitudeM = CoordinateSystem.FtToM(8000);
            ExecuteIngress(deltaTime);
            // After attack, dive back down and egress
            if (AltitudeM > CoordinateSystem.FtToM(7000))
                CurrentBehavior = AircraftBehavior.EgressRetreat;
        }
    }

    private void ExecuteOrbit(double deltaTime)
    {
        // CAP patrol behavior: orbit within assigned sector
        if (PatrolSectorCenter.HasValue && PatrolSectorRadiusM > 0)
        {
            ExecuteCapPatrol(deltaTime);
        }
        else
        {
            // Simple orbit: constantly turn
            RequestedHeadingDeg = (HeadingDeg + 2.0 * deltaTime + 360) % 360;
        }
    }

    /// <summary>
    /// Execute CAP patrol behavior within assigned sector.
    /// Implements sector boundary enforcement and autonomous target detection.
    /// </summary>
    private void ExecuteCapPatrol(double deltaTime)
    {
        if (!PatrolSectorCenter.HasValue) return;

        Vec2 sectorCenter = PatrolSectorCenter.Value;
        double distanceToCenter = Position.DistanceTo(sectorCenter);
        
        // Check if on station (within sector)
        if (!IsOnStation && distanceToCenter < PatrolSectorRadiusM)
        {
            IsOnStation = true;
            OnStationTime = DateTime.UtcNow;
            
            // Send on-station arrival report
            SendOnStationReport();
        }

        // If intercepting a target, skip normal patrol behavior
        // Intercept course will be updated by external system (FriendlySupportDirector or command handler)
        if (!string.IsNullOrEmpty(InterceptTargetTrackId))
        {
            // Intercept mode: maintain full speed and current heading
            // Course updates are handled externally via SetInterceptCourse
            RequestedSpeedMps = FlightModel.MaxSpeedMps;
            return;
        }

        // Sector boundary enforcement: turn back if approaching edge
        double boundaryThreshold = PatrolSectorRadiusM * 0.85; // Turn at 85% of radius
        if (distanceToCenter > boundaryThreshold)
        {
            // Turn toward sector center
            RequestedHeadingDeg = Position.HeadingTo(sectorCenter);
            RequestedAltitudeM = CoordinateSystem.FtToM(25000); // Standard CAP altitude
            RequestedSpeedMps = FlightModel.MaxSpeedMps * 0.75; // Cruise speed
        }
        else
        {
            // Normal patrol: fly racetrack pattern
            // Simple implementation: orbit around sector center
            Vec2 toCenter = sectorCenter - Position;
            double bearingToCenter = Position.HeadingTo(sectorCenter);
            
            // Fly perpendicular to radius vector for circular patrol
            RequestedHeadingDeg = NormalizeHeading(bearingToCenter + 90);
            RequestedAltitudeM = CoordinateSystem.FtToM(25000);
            RequestedSpeedMps = FlightModel.MaxSpeedMps * 0.75;
        }
    }

    /// <summary>
    /// Set intercept target and transition to intercept behavior.
    /// Requirement 3.1: WHEN a player issues an intercept command, THE CAP_Fighter SHALL change behavior to intercept.
    /// </summary>
    public void SetInterceptTarget(string targetTrackId)
    {
        if (string.IsNullOrEmpty(targetTrackId))
            return;

        InterceptTargetTrackId = targetTrackId;
        
        // Reset tally report for new target
        ResetTallyReport();
        
        // Send Wilco acknowledgment
        SendWilcoAcknowledgment($"intercepting {targetTrackId}");
    }

    /// <summary>
    /// Calculate and set intercept course toward a target track.
    /// Implements lead pursuit for optimal intercept geometry.
    /// Requirement 3.3: THE CAP_Fighter SHALL calculate an intercept course to the target Track.
    /// </summary>
    public void SetInterceptCourse(Vec2 targetPosition, Vec2 targetVelocity)
    {
        // Calculate intercept point using proportional navigation
        Vec2 relativePosition = targetPosition - Position;
        double timeToIntercept = CalculateInterceptTime(relativePosition, targetVelocity, FlightModel.MaxSpeedMps);
        
        if (timeToIntercept > 0)
        {
            // Lead the target
            Vec2 interceptPoint = targetPosition + targetVelocity * timeToIntercept;
            RequestedHeadingDeg = Position.HeadingTo(interceptPoint);
            RequestedAltitudeM = CoordinateSystem.FtToM(25000); // Match typical engagement altitude
            RequestedSpeedMps = FlightModel.MaxSpeedMps; // Full speed for intercept
        }
        else
        {
            // Direct pursuit if intercept calculation fails
            RequestedHeadingDeg = Position.HeadingTo(targetPosition);
            RequestedSpeedMps = FlightModel.MaxSpeedMps;
        }
    }

    /// <summary>
    /// Clear intercept target and resume patrol behavior.
    /// Requirement 3.5: IF the target Track is destroyed, THEN THE CAP_Fighter SHALL resume patrol.
    /// </summary>
    public void ResumePatrol()
    {
        InterceptTargetTrackId = null;
        ResetTallyReport();
        
        if (CurrentBehavior != AircraftBehavior.OrbitPatrol)
        {
            CurrentBehavior = AircraftBehavior.OrbitPatrol;
        }
    }

    /// <summary>
    /// Calculate time to intercept using relative velocity.
    /// Returns -1 if intercept is not possible (target moving away faster than we can chase).
    /// </summary>
    private double CalculateInterceptTime(Vec2 relativePosition, Vec2 targetVelocity, double interceptorSpeed)
    {
        // Solve for time when |relativePosition + targetVelocity * t - interceptorVelocity * t| = 0
        // Simplified: assume we fly directly toward intercept point
        double range = relativePosition.Length;
        double targetSpeed = targetVelocity.Length;
        
        // Law of cosines approach for intercept
        // This is a simplified calculation; full solution requires solving quadratic equation
        double closingSpeed = interceptorSpeed - targetSpeed * 0.5; // Approximate
        
        if (closingSpeed <= 0) return -1; // Can't catch target
        
        return range / closingSpeed;
    }

    /// <summary>
    /// Check if a position is within the assigned patrol sector.
    /// </summary>
    public bool IsPositionInSector(Vec2 position)
    {
        if (!PatrolSectorCenter.HasValue || PatrolSectorRadiusM <= 0)
            return false;
        
        return position.DistanceTo(PatrolSectorCenter.Value) <= PatrolSectorRadiusM;
    }

    /// <summary>
    /// Detect hostile tracks within patrol sector and engagement range.
    /// Returns true if a valid target is detected within sector.
    /// </summary>
    public bool DetectHostileInSector(IEnumerable<Entity> hostileEntities, out Entity? detectedTarget)
    {
        detectedTarget = null;
        
        if (!PatrolSectorCenter.HasValue || PatrolSectorRadiusM <= 0)
            return false;

        double detectionRangeM = CoordinateSystem.NmToMeters(40); // F-16 radar range ~40nm
        
        foreach (var hostile in hostileEntities)
        {
            if (!hostile.IsActive) continue;
            
            double rangeToTarget = Position.DistanceTo(hostile.Position);
            bool inDetectionRange = rangeToTarget <= detectionRangeM;
            bool inSector = IsPositionInSector(hostile.Position);
            
            if (inDetectionRange && inSector)
            {
                detectedTarget = hostile;
                return true;
            }
        }
        
        return false;
    }

    // ── Weapon Engagement Logic (Task 1.3) ───────────────────────────

    /// <summary>
    /// Select appropriate weapon for target based on range.
    /// Returns weapon type and whether engagement is possible.
    /// </summary>
    public (WeaponType weapon, bool canEngage) SelectWeaponForTarget(double rangeToTargetNm)
    {
        // Check if Winchester
        if (IsWinchester)
            return (WeaponType.None, false);

        // BVR engagement: use AIM-120 if available and in range
        if (rangeToTargetNm >= BvrRangeThresholdNm)
        {
            if (Aim120Count > 0 && IsInAim120Range(rangeToTargetNm))
                return (WeaponType.Aim120, true);
            
            return (WeaponType.None, false);
        }

        // WVR engagement: prefer AIM-9 if available and in range
        if (Aim9Count > 0 && IsInAim9Range(rangeToTargetNm))
            return (WeaponType.Aim9, true);

        // Fallback to AIM-120 if AIM-9 not available but AIM-120 is in range
        if (Aim120Count > 0 && IsInAim120Range(rangeToTargetNm))
            return (WeaponType.Aim120, true);

        return (WeaponType.None, false);
    }

    /// <summary>
    /// Check if target is within AIM-120 engagement range.
    /// </summary>
    public bool IsInAim120Range(double rangeToTargetNm)
    {
        return rangeToTargetNm >= Aim120MinRangeNm && rangeToTargetNm <= Aim120MaxRangeNm;
    }

    /// <summary>
    /// Check if target is within AIM-9 engagement range.
    /// </summary>
    public bool IsInAim9Range(double rangeToTargetNm)
    {
        return rangeToTargetNm >= Aim9MinRangeNm && rangeToTargetNm <= Aim9MaxRangeNm;
    }

    /// <summary>
    /// Check if target is within any weapon engagement range.
    /// </summary>
    public bool IsTargetInWeaponRange(double rangeToTargetNm)
    {
        if (Aim120Count > 0 && IsInAim120Range(rangeToTargetNm))
            return true;
        
        if (Aim9Count > 0 && IsInAim9Range(rangeToTargetNm))
            return true;

        return false;
    }

    /// <summary>
    /// Expend a weapon of the specified type.
    /// Returns true if weapon was available and expended.
    /// </summary>
    public bool ExpendWeapon(WeaponType weaponType)
    {
        switch (weaponType)
        {
            case WeaponType.Aim120:
                if (Aim120Count > 0)
                {
                    Aim120Count--;
                    return true;
                }
                return false;

            case WeaponType.Aim9:
                if (Aim9Count > 0)
                {
                    Aim9Count--;
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Check if fighter should RTB due to Winchester condition.
    /// Returns true if Winchester and should return to base.
    /// </summary>
    public bool ShouldRtbDueToWinchester()
    {
        return IsWinchester && CurrentBehavior != AircraftBehavior.EgressRetreat;
    }

    private void ExecuteEcmStandoff()
    {
        Vec2 missionAnchor = ResolveMissionAnchor();
        double rangeNm = CoordinateSystem.MetersToNm(Position.DistanceTo(missionAnchor));
        RequestedAltitudeM = CoordinateSystem.FtToM(28000);
        RequestedSpeedMps = FlightModel.MaxSpeedMps * 0.78;

        if (rangeNm > 46)
        {
            RequestedHeadingDeg = Position.HeadingTo(missionAnchor);
            return;
        }

        RequestedHeadingDeg = NormalizeHeading(RadarLockBearingDeg + 100);
    }

    private void ExecuteEscortCover()
    {
        Vec2 missionAnchor = ResolveMissionAnchor();
        double rangeNm = CoordinateSystem.MetersToNm(Position.DistanceTo(missionAnchor));
        RequestedAltitudeM = CoordinateSystem.FtToM(26000);
        RequestedSpeedMps = FlightModel.MaxSpeedMps * 0.94;

        if (rangeNm > 24)
        {
            RequestedHeadingDeg = Position.HeadingTo(missionAnchor);
            return;
        }

        RequestedHeadingDeg = NormalizeHeading(RadarLockBearingDeg + 45);
    }

    private void ExecuteSead()
    {
        Vec2 missionAnchor = ResolveMissionAnchor();
        double rangeNm = CoordinateSystem.MetersToNm(Position.DistanceTo(missionAnchor));
        RequestedAltitudeM = CoordinateSystem.FtToM(rangeNm > 22 ? 22000 : 18000);
        RequestedSpeedMps = FlightModel.MaxSpeedMps * 0.88;

        if (rangeNm <= 16)
        {
            GameSessionLogger.Current?.OnBehaviorChanged(Id, Designation, nameof(AircraftBehavior.SEAD), nameof(AircraftBehavior.EgressRetreat), "sead_min_range");
            CurrentBehavior = AircraftBehavior.EgressRetreat;
            RequestedHeadingDeg = NormalizeHeading(HeadingDeg + 180);
            return;
        }

        RequestedHeadingDeg = Position.HeadingTo(missionAnchor);
    }

    private void DeployChaff()
    {
        if (IsChaffActive || ChaffCharges <= 0)
            return;

        ChaffCharges--;
        ChaffActiveUntilUtc = DateTime.UtcNow.AddSeconds(4);
    }

    private void DeployFlares()
    {
        if (IsFlareActive || FlareCharges <= 0)
            return;

        FlareCharges--;
        FlareActiveUntilUtc = DateTime.UtcNow.AddSeconds(3);
    }

    private AircraftBehavior ChooseRadarThreatBehavior()
    {
        if (HasARMCapability || Role == AircraftRole.SEAD)
            return AircraftBehavior.SEAD;

        return Role switch
        {
            AircraftRole.ECMEscort => AircraftBehavior.ECMStandoff,
            AircraftRole.Fighter => HardLockDetected ? AircraftBehavior.EscortCover : AircraftBehavior.DefensiveBeam,
            AircraftRole.Striker => AircraftBehavior.TerrainFollowing,
            _ => AircraftBehavior.DefensiveBeam
        };
    }

    private static bool IsThreatResponseBehavior(AircraftBehavior behavior) => behavior is
        AircraftBehavior.EvasiveManeuver or
        AircraftBehavior.DefensiveBeam or
        AircraftBehavior.ECMStandoff or
        AircraftBehavior.EscortCover or
        AircraftBehavior.SEAD or
        AircraftBehavior.TerrainFollowing;

    private Vec2 ResolveMissionAnchor() => ObjectivePosition ?? TargetWaypoint ?? Vec2.Zero;

    private static double NormalizeHeading(double headingDeg) => (headingDeg % 360 + 360) % 360;

    // ── Radio Communication Methods (Task 3.1) ────────────────────────

    /// <summary>
    /// Send Wilco (will comply) acknowledgment when receiving tasking order.
    /// Requirement 2.1: WHEN a CAP_Fighter receives a tasking order, THE CAP_Fighter SHALL acknowledge with a Wilco message.
    /// </summary>
    public void SendWilcoAcknowledgment(string taskDescription)
    {
        if (CommManager == null || string.IsNullOrEmpty(CallSign)) return;

        var speaker = RadioRules.CreateFriendlySupportProfile(
            CallSign,
            "CAP FIGHTER",
            CallSign,
            Designation);

        var message = CommManager.CreateMessage(
            speaker,
            RadioChannel.CommandNet,
            $"WILCO, {taskDescription}",
            MessagePriority.Routine,
            MessageType.StatusReport,
            recipient: "ALPHA ACTUAL",
            canReply: false,
            staticLevel: 0.15);

        CommManager.Queue(message);
    }

    /// <summary>
    /// Send on-station arrival report when entering patrol sector.
    /// Requirement 2.7: WHEN a CAP_Fighter arrives on station, THE CAP_Fighter SHALL report on-station with sector designation.
    /// </summary>
    private void SendOnStationReport()
    {
        if (CommManager == null || string.IsNullOrEmpty(CallSign) || _hasReportedOnStation) return;
        if (string.IsNullOrEmpty(PatrolSectorId)) return;

        _hasReportedOnStation = true;

        var speaker = RadioRules.CreateFriendlySupportProfile(
            CallSign,
            "CAP FIGHTER",
            CallSign,
            Designation);

        var message = CommManager.CreateMessage(
            speaker,
            RadioChannel.CommandNet,
            $"{CallSign} on station, {PatrolSectorId}, ready for tasking",
            MessagePriority.Routine,
            MessageType.StatusReport,
            recipient: "ALPHA ACTUAL",
            canReply: false,
            staticLevel: 0.15);

        CommManager.Queue(message);
    }

    /// <summary>
    /// Send Tally report when achieving visual/radar contact with target.
    /// Requirement 2.2: WHEN a CAP_Fighter achieves Tally on a target, THE CAP_Fighter SHALL report visual contact via radio.
    /// </summary>
    public void SendTallyReport(string targetTrackId)
    {
        if (CommManager == null || string.IsNullOrEmpty(CallSign)) return;
        if (string.IsNullOrEmpty(targetTrackId)) return;

        // Only send tally once per target
        if (_lastTallyTargetId == targetTrackId) return;
        _lastTallyTargetId = targetTrackId;

        var speaker = RadioRules.CreateFriendlySupportProfile(
            CallSign,
            "CAP FIGHTER",
            CallSign,
            Designation);

        var message = CommManager.CreateMessage(
            speaker,
            RadioChannel.CommandNet,
            $"TALLY, {targetTrackId}, engaging",
            MessagePriority.Priority,
            MessageType.StatusReport,
            recipient: "ALPHA ACTUAL",
            canReply: false,
            staticLevel: 0.15);

        CommManager.Queue(message);
    }

    /// <summary>
    /// Reset tally report flag when target changes or is lost.
    /// Allows sending new tally report for different targets.
    /// </summary>
    public void ResetTallyReport()
    {
        _lastTallyTargetId = null;
    }

    // ── Engagement Radio Communications (Task 3.2) ────────────────────

    /// <summary>
    /// Send Fox-3 launch report with target track number.
    /// Requirement 2.3: WHEN a CAP_Fighter launches a weapon, THE CAP_Fighter SHALL report Fox-3 with target track number.
    /// </summary>
    public void SendFox3Report(string targetTrackId, WeaponType weaponType)
    {
        if (CommManager == null || string.IsNullOrEmpty(CallSign)) return;
        if (string.IsNullOrEmpty(targetTrackId)) return;

        string weaponCall = weaponType switch
        {
            WeaponType.Aim120 => "FOX-3",
            WeaponType.Aim9 => "FOX-2",
            _ => "WEAPON AWAY"
        };

        var speaker = RadioRules.CreateFriendlySupportProfile(
            CallSign,
            "CAP FIGHTER",
            CallSign,
            Designation);

        var message = CommManager.CreateMessage(
            speaker,
            RadioChannel.CommandNet,
            $"{weaponCall}, {targetTrackId}",
            MessagePriority.Priority,
            MessageType.StatusReport,
            recipient: "ALPHA ACTUAL",
            canReply: false,
            staticLevel: 0.15);

        CommManager.Queue(message);
    }

    /// <summary>
    /// Send Splash report on target destruction.
    /// Requirement 2.4: WHEN a CAP_Fighter destroys a target, THE CAP_Fighter SHALL report Splash with target track number.
    /// </summary>
    public void SendSplashReport(string targetTrackId)
    {
        if (CommManager == null || string.IsNullOrEmpty(CallSign)) return;
        if (string.IsNullOrEmpty(targetTrackId)) return;

        var speaker = RadioRules.CreateFriendlySupportProfile(
            CallSign,
            "CAP FIGHTER",
            CallSign,
            Designation);

        var message = CommManager.CreateMessage(
            speaker,
            RadioChannel.CommandNet,
            $"SPLASH, {targetTrackId}, target destroyed",
            MessagePriority.Priority,
            MessageType.StatusReport,
            recipient: "ALPHA ACTUAL",
            canReply: false,
            staticLevel: 0.15);

        CommManager.Queue(message);
    }

    /// <summary>
    /// Send Bingo fuel report and RTB notification.
    /// Requirement 2.5: WHEN a CAP_Fighter reaches Bingo_Fuel, THE CAP_Fighter SHALL report fuel state and RTB intent.
    /// </summary>
    public void SendBingoFuelReport()
    {
        if (CommManager == null || string.IsNullOrEmpty(CallSign)) return;

        var speaker = RadioRules.CreateFriendlySupportProfile(
            CallSign,
            "CAP FIGHTER",
            CallSign,
            Designation);

        var message = CommManager.CreateMessage(
            speaker,
            RadioChannel.CommandNet,
            $"{CallSign} BINGO fuel, RTB",
            MessagePriority.Priority,
            MessageType.StatusReport,
            recipient: "ALPHA ACTUAL",
            canReply: false,
            staticLevel: 0.15);

        CommManager.Queue(message);
    }

    /// <summary>
    /// Send Winchester report and RTB notification.
    /// Requirement 2.6: WHEN a CAP_Fighter reaches Winchester, THE CAP_Fighter SHALL report weapons state and RTB intent.
    /// </summary>
    public void SendWinchesterReport()
    {
        if (CommManager == null || string.IsNullOrEmpty(CallSign)) return;

        var speaker = RadioRules.CreateFriendlySupportProfile(
            CallSign,
            "CAP FIGHTER",
            CallSign,
            Designation);

        var message = CommManager.CreateMessage(
            speaker,
            RadioChannel.CommandNet,
            $"{CallSign} WINCHESTER, RTB",
            MessagePriority.Priority,
            MessageType.StatusReport,
            recipient: "ALPHA ACTUAL",
            canReply: false,
            staticLevel: 0.15);

        CommManager.Queue(message);
    }

    /// <summary>
    /// Send Defensive report when under threat.
    /// Requirement 2.8: IF a CAP_Fighter is under threat, THEN THE CAP_Fighter SHALL report Defensive status.
    /// </summary>
    public void SendDefensiveReport()
    {
        if (CommManager == null || string.IsNullOrEmpty(CallSign)) return;

        var speaker = RadioRules.CreateFriendlySupportProfile(
            CallSign,
            "CAP FIGHTER",
            CallSign,
            Designation);

        var message = CommManager.CreateMessage(
            speaker,
            RadioChannel.CommandNet,
            $"{CallSign} DEFENSIVE, engaged",
            MessagePriority.Flash,
            MessageType.StatusReport,
            recipient: "ALPHA ACTUAL",
            canReply: false,
            staticLevel: 0.15);

        CommManager.Queue(message);
    }

    // ── Static factory methods ────────────────────────────────────────

    public static Aircraft CreateFromType(string designation, Affiliation affiliation)
    {
        return designation.ToUpperInvariant() switch
        {
            "SU-24" or "SU24" or "FENCER" => new Aircraft
            {
                Designation = "SU-24 FENCER",
                Affiliation = affiliation,
                Type = EntityType.Aircraft,
                Role = AircraftRole.Striker,
                RcsM2 = 12.0,
                FuelCapacityKg = 11000,
                FuelBurnRateKgSec = 3.5,
                BingoFuelKg = 1500,
                FlightModel = FlightModel.Bomber,
            },
            "MIG-29" or "MIG29" or "FULCRUM" => new Aircraft
            {
                Designation = "MiG-29 FULCRUM",
                Affiliation = affiliation,
                Type = EntityType.Aircraft,
                Role = AircraftRole.Fighter,
                RcsM2 = 5.0,
                FuelCapacityKg = 4500,
                FuelBurnRateKgSec = 2.8,
                BingoFuelKg = 700,
                FlightModel = FlightModel.Fighter,
            },
            "SU-25" or "SU25" or "FROGFOOT" => new Aircraft
            {
                Designation = "Su-25 FROGFOOT",
                Affiliation = affiliation,
                Type = EntityType.Aircraft,
                Role = AircraftRole.Striker,
                RcsM2 = 8.0,
                FuelCapacityKg = 3600,
                FuelBurnRateKgSec = 2.2,
                BingoFuelKg = 600,
                FlightModel = new FlightModel
                {
                    MaxTurnRateDegSec = 5.0,
                    MaxClimbRateMps = 60.0,
                    MaxDescentRateMps = 80.0,
                    MaxAccelMps2 = 10.0,
                    MaxDecelMps2 = 15.0,
                    MinSpeedMps = 70.0,
                    MaxSpeedMps = 320.0
                }
            },
            "EA-6" or "PROWLER" or "ECM" => new Aircraft
            {
                Designation = "EA-6B PROWLER",
                Affiliation = affiliation,
                Type = EntityType.Aircraft,
                Role = AircraftRole.ECMEscort,
                RcsM2 = 10.0,
                HasECM = true,
                EcmPower = 100000, // 100kW ERP
                FuelCapacityKg = 7700,
                FuelBurnRateKgSec = 2.5,
                BingoFuelKg = 1000,
                FlightModel = FlightModel.Bomber,
            },
            "DRONE" or "UAV" or "MQ-9" => new Aircraft
            {
                Designation = "MQ-9 DRONE",
                Affiliation = affiliation,
                Type = EntityType.Drone,
                Role = AircraftRole.Recon,
                RcsM2 = 0.5,
                FuelCapacityKg = 1000,
                FuelBurnRateKgSec = 0.3,
                BingoFuelKg = 150,
                FlightModel = FlightModel.Drone,
            },
            "F-16" or "F16" or "VIPER" => new Aircraft
            {
                Designation = "F-16C VIPER",
                Affiliation = affiliation,
                Type = EntityType.Aircraft,
                Role = AircraftRole.Fighter,
                RcsM2 = 1.2,
                FuelCapacityKg = 3200,
                FuelBurnRateKgSec = 2.1,
                BingoFuelKg = 500,
                FlightModel = FlightModel.Fighter,
            },
            _ => new Aircraft
            {
                Designation = designation,
                Affiliation = affiliation,
                Type = EntityType.Aircraft,
                Role = AircraftRole.Fighter,
                RcsM2 = 5.0,
                FuelCapacityKg = 4000,
                FuelBurnRateKgSec = 2.5,
                BingoFuelKg = 700,
                FlightModel = FlightModel.Fighter,
            }
        };
    }
}

/// <summary>Global random instance for simulation (seed-able for testing)</summary>
public static class SimulationRandom
{
    private static Random _rng = new();
    public static Random Instance => _rng;
    public static void Seed(int seed) => _rng = new Random(seed);
}
