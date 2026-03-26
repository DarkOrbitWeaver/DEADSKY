using DEADSKY.Core.Physics;

namespace DEADSKY.Core.Entities;

public enum AircraftBehavior
{
    IngressAttack,       // Flying toward target
    EgressRetreat,       // Running away
    OrbitPatrol,         // Racetrack/orbit pattern
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

    // ── Threat awareness (what the aircraft "knows") ──────────────────
    public bool RadarLockDetected { get; set; }    // RWR is screaming
    public double RadarLockBearingDeg { get; set; }
    public DateTime? RadarLockDetectedTime { get; set; }
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
                break;

            case AircraftBehavior.EgressRetreat:
                ExecuteEgress(deltaTime);
                break;

            case AircraftBehavior.EvasiveManeuver:
                ExecuteEvasion(deltaTime);
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

            case AircraftBehavior.Feint:
                // Act like IngressAttack but stop at a certain range
                double range = Position.Length;
                if (range < CoordinateSystem.NmToMeters(25))
                {
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
        bool missileThreatHot = MissileInbound &&
                                MissileInboundDetectedTime.HasValue &&
                                now - MissileInboundDetectedTime.Value <= TimeSpan.FromSeconds(8);

        ECMActive = HasECM && (radarSpikeHot || missileThreatHot);

        if (radarSpikeHot && ChaffCharges > 0)
            DeployChaff();

        if (missileThreatHot && FlareCharges > 0)
            DeployFlares();

        if (missileThreatHot)
        {
            if (CurrentBehavior != AircraftBehavior.EvasiveManeuver)
                _behaviorBeforeThreatReaction = CurrentBehavior;

            CurrentBehavior = AircraftBehavior.EvasiveManeuver;
            _threatReactionUntilUtc = now.AddSeconds(5);
            return;
        }

        if (CurrentBehavior == AircraftBehavior.EvasiveManeuver &&
            _behaviorBeforeThreatReaction.HasValue &&
            _threatReactionUntilUtc.HasValue &&
            now >= _threatReactionUntilUtc.Value)
        {
            CurrentBehavior = _behaviorBeforeThreatReaction.Value;
            _behaviorBeforeThreatReaction = null;
            _threatReactionUntilUtc = null;
        }

        if (!radarSpikeHot)
            RadarLockDetected = false;

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

    private void ExecutePopUp(double deltaTime)
    {
        double rangeM = Position.Length;
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
        // Simple orbit: constantly turn
        RequestedHeadingDeg = (HeadingDeg + 2.0 * deltaTime + 360) % 360;
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
