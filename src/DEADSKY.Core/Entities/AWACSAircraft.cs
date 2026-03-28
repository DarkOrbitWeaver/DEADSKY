using DEADSKY.Core.Physics;

namespace DEADSKY.Core.Entities;

/// <summary>
/// AWACS (Airborne Warning and Control System) aircraft like the E-3 Sentry.
/// Provides enhanced radar coverage and flies racetrack orbit patterns.
/// Requirement 4.1: AWACS entity with position, velocity, altitude, and orbit pattern.
/// Requirement 4.8: AWACS has realistic RCS and vulnerability characteristics.
/// </summary>
public class AWACSAircraft : Aircraft
{
    // ── Racetrack orbit pattern properties ───────────────────────────
    /// <summary>Length of straight legs in the racetrack pattern (nautical miles)</summary>
    public double RacetrackLegLengthNm { get; set; }
    
    /// <summary>Heading for the racetrack legs (degrees)</summary>
    public double RacetrackHeadingDeg { get; set; }
    
    /// <summary>Radius of the turns in the racetrack pattern (nautical miles)</summary>
    public double RacetrackTurnRadiusNm { get; set; }

    // ── Enhanced radar coverage properties ────────────────────────────
    /// <summary>Multiplier for friendly radar range when AWACS is active (default 1.3 for 30% boost)</summary>
    public double RadarRangeMultiplier { get; set; } = 1.3;
    
    /// <summary>Radius of AWACS radar coverage area (nautical miles)</summary>
    public double RadarCoverageRadiusNm { get; set; }

    public AWACSAircraft()
    {
        // Set E-3 Sentry physical characteristics
        // Requirement 4.1: E-3 entity at 30,000 feet altitude
        MaxSpeedKts = 530;           // Max speed ~530 kts
        CruiseSpeedKts = 460;        // Cruise speed ~460 kts
        CruiseAltitudeFt = 30000;    // Standard AWACS patrol altitude
        MaxAltitudeFt = 42000;       // Service ceiling
        TurnRateDegreesPerSec = 1.5; // Large aircraft, slow turn rate
        
        // Set entity type and designation
        Type = EntityType.Aircraft;
        Affiliation = Affiliation.Friendly;
        Designation = "E-3 SENTRY AWACS";
        Role = AircraftRole.Recon;   // AWACS is reconnaissance/command role
        
        // Set realistic RCS for large aircraft
        // Requirement 4.8: Realistic RCS and vulnerability characteristics
        RcsM2 = 40.0; // Large aircraft, high radar signature
        
        // Set fuel characteristics for long endurance
        FuelCapacityKg = 118000; // E-3 has large fuel capacity for 8+ hour missions
        FuelBurnRateKgSec = 3.5; // Moderate burn rate for 4 turbofan engines
        BingoFuelKg = 20000;     // Conservative bingo fuel for safe RTB
        
        // Set flight model for large transport aircraft
        FlightModel = new FlightModel
        {
            MaxTurnRateDegSec = 1.5,
            MaxClimbRateMps = 15.0,
            MaxDescentRateMps = 20.0,
            MaxAccelMps2 = 3.0,
            MaxDecelMps2 = 5.0,
            MinSpeedMps = CoordinateSystem.KtsToMps(200), // ~200 kts minimum
            MaxSpeedMps = CoordinateSystem.KtsToMps(530)  // ~530 kts maximum
        };
        
        // Initialize requested state to cruise parameters
        RequestedAltitudeM = CoordinateSystem.FtToM(CruiseAltitudeFt);
        RequestedSpeedMps = CoordinateSystem.KtsToMps(CruiseSpeedKts);
    }
    
    // ── Properties for compatibility with existing Aircraft code ──────
    // These properties are used by the task description but may need to be
    // added to the base Aircraft class if they don't exist
    
    /// <summary>Maximum speed in knots</summary>
    public double MaxSpeedKts { get; init; }
    
    /// <summary>Cruise speed in knots</summary>
    public double CruiseSpeedKts { get; init; }
    
    /// <summary>Cruise altitude in feet</summary>
    public double CruiseAltitudeFt { get; init; }
    
    /// <summary>Maximum altitude in feet</summary>
    public double MaxAltitudeFt { get; init; }
    
    /// <summary>Turn rate in degrees per second</summary>
    public double TurnRateDegreesPerSec { get; init; }

    // ── Racetrack pattern state tracking ──────────────────────────────
    private enum RacetrackSegment
    {
        Leg1,      // First straight leg
        Turn1,     // First 180-degree turn
        Leg2,      // Second straight leg
        Turn2      // Second 180-degree turn
    }

    private RacetrackSegment _currentSegment = RacetrackSegment.Leg1;
    private Vec2 _racetrackCenter = Vec2.Zero;

    /// <summary>
    /// Override Update to execute AWACS orbit behavior when in orbit mode.
    /// Requirement 4.2: Implement racetrack pattern flight logic.
    /// </summary>
    public override void Update(double deltaTime)
    {
        if (!IsActive) return;

        // Execute AWACS-specific orbit behavior when in orbit mode
        if (CurrentBehavior == AircraftBehavior.OrbitPatrol)
        {
            ExecuteAwacsOrbit(deltaTime);
        }

        // Call base update for standard aircraft behavior (fuel, physics, etc.)
        base.Update(deltaTime);
    }

    /// <summary>
    /// Execute AWACS racetrack orbit pattern.
    /// Maintains 30,000ft altitude and flies a racetrack pattern around the center position.
    /// Requirement 4.2: Implement racetrack pattern flight logic for AWACS aircraft.
    /// </summary>
    private void ExecuteAwacsOrbit(double deltaTime)
    {
        // Ensure racetrack parameters are configured
        if (RacetrackLegLengthNm <= 0 || RacetrackTurnRadiusNm <= 0)
        {
            // Fallback to simple orbit if racetrack not configured
            RequestedHeadingDeg = (HeadingDeg + 2.0 * deltaTime + 360) % 360;
            RequestedAltitudeM = CoordinateSystem.FtToM(CruiseAltitudeFt);
            RequestedSpeedMps = CoordinateSystem.KtsToMps(CruiseSpeedKts);
            return;
        }

        // Initialize racetrack center on first execution
        if (_racetrackCenter.Length < 0.1) // Check if center is at origin (uninitialized)
        {
            _racetrackCenter = Position;
        }

        // Maintain cruise altitude and speed
        RequestedAltitudeM = CoordinateSystem.FtToM(CruiseAltitudeFt);
        RequestedSpeedMps = CoordinateSystem.KtsToMps(CruiseSpeedKts);

        // Calculate current segment target and update heading
        switch (_currentSegment)
        {
            case RacetrackSegment.Leg1:
                ExecuteLeg1(deltaTime);
                break;
            case RacetrackSegment.Turn1:
                ExecuteTurn1(deltaTime);
                break;
            case RacetrackSegment.Leg2:
                ExecuteLeg2(deltaTime);
                break;
            case RacetrackSegment.Turn2:
                ExecuteTurn2(deltaTime);
                break;
        }
    }

    /// <summary>
    /// Execute first straight leg of racetrack pattern.
    /// Flies straight at RacetrackHeadingDeg for RacetrackLegLengthNm.
    /// </summary>
    private void ExecuteLeg1(double deltaTime)
    {
        // Fly straight at racetrack heading
        RequestedHeadingDeg = RacetrackHeadingDeg;

        // Calculate leg start and end points
        Vec2 legStart = CalculateLeg1Start();
        Vec2 legEnd = CalculateLeg1End();
        double legLengthM = CoordinateSystem.NmToMeters(RacetrackLegLengthNm);

        // Check if we've completed this leg
        double distanceToEnd = Position.DistanceTo(legEnd);
        if (distanceToEnd < 500) // Within 500m of end point
        {
            _currentSegment = RacetrackSegment.Turn1;
        }
    }

    /// <summary>
    /// Execute first 180-degree turn of racetrack pattern.
    /// Turns from RacetrackHeadingDeg to RacetrackHeadingDeg + 180.
    /// </summary>
    private void ExecuteTurn1(double deltaTime)
    {
        // Calculate turn center (to the right of leg 1 end point)
        Vec2 leg1End = CalculateLeg1End();
        double turnCenterBearing = RacetrackHeadingDeg + 90; // 90 degrees right
        double turnRadiusM = CoordinateSystem.NmToMeters(RacetrackTurnRadiusNm);
        Vec2 turnOffset = CoordinateSystem.FromBearingRange(turnCenterBearing, RacetrackTurnRadiusNm);
        Vec2 turnCenter = leg1End + turnOffset;

        // Calculate target heading for smooth turn
        double targetHeading = NormalizeHeading(RacetrackHeadingDeg + 180);
        
        // Gradually turn toward target heading
        double headingDiff = NormalizeHeadingDiff(targetHeading - HeadingDeg);
        if (Math.Abs(headingDiff) < 5) // Within 5 degrees of target
        {
            _currentSegment = RacetrackSegment.Leg2;
        }
        else
        {
            // Turn at aircraft turn rate
            double turnAmount = TurnRateDegreesPerSec * deltaTime;
            RequestedHeadingDeg = NormalizeHeading(HeadingDeg + Math.Sign(headingDiff) * turnAmount);
        }
    }

    /// <summary>
    /// Execute second straight leg of racetrack pattern.
    /// Flies straight at RacetrackHeadingDeg + 180 for RacetrackLegLengthNm.
    /// </summary>
    private void ExecuteLeg2(double deltaTime)
    {
        // Fly straight at opposite heading
        RequestedHeadingDeg = NormalizeHeading(RacetrackHeadingDeg + 180);

        // Calculate leg end point
        Vec2 legEnd = CalculateLeg2End();

        // Check if we've completed this leg
        double distanceToEnd = Position.DistanceTo(legEnd);
        if (distanceToEnd < 500) // Within 500m of end point
        {
            _currentSegment = RacetrackSegment.Turn2;
        }
    }

    /// <summary>
    /// Execute second 180-degree turn of racetrack pattern.
    /// Turns from RacetrackHeadingDeg + 180 back to RacetrackHeadingDeg.
    /// </summary>
    private void ExecuteTurn2(double deltaTime)
    {
        // Calculate turn center (to the right of leg 2 end point)
        Vec2 leg2End = CalculateLeg2End();
        double turnCenterBearing = NormalizeHeading(RacetrackHeadingDeg + 180 + 90); // 90 degrees right
        double turnRadiusM = CoordinateSystem.NmToMeters(RacetrackTurnRadiusNm);
        Vec2 turnOffset = CoordinateSystem.FromBearingRange(turnCenterBearing, RacetrackTurnRadiusNm);
        Vec2 turnCenter = leg2End + turnOffset;

        // Calculate target heading for smooth turn
        double targetHeading = RacetrackHeadingDeg;
        
        // Gradually turn toward target heading
        double headingDiff = NormalizeHeadingDiff(targetHeading - HeadingDeg);
        if (Math.Abs(headingDiff) < 5) // Within 5 degrees of target
        {
            _currentSegment = RacetrackSegment.Leg1;
        }
        else
        {
            // Turn at aircraft turn rate
            double turnAmount = TurnRateDegreesPerSec * deltaTime;
            RequestedHeadingDeg = NormalizeHeading(HeadingDeg + Math.Sign(headingDiff) * turnAmount);
        }
    }

    // ── Racetrack geometry calculation helpers ────────────────────────

    /// <summary>Calculate start point of leg 1 (center minus half leg length along heading)</summary>
    private Vec2 CalculateLeg1Start()
    {
        double halfLegNm = RacetrackLegLengthNm / 2.0;
        double bearingDeg = NormalizeHeading(RacetrackHeadingDeg + 180); // Opposite direction
        Vec2 offset = CoordinateSystem.FromBearingRange(bearingDeg, halfLegNm);
        return _racetrackCenter + offset;
    }

    /// <summary>Calculate end point of leg 1 (center plus half leg length along heading)</summary>
    private Vec2 CalculateLeg1End()
    {
        double halfLegNm = RacetrackLegLengthNm / 2.0;
        Vec2 offset = CoordinateSystem.FromBearingRange(RacetrackHeadingDeg, halfLegNm);
        return _racetrackCenter + offset;
    }

    /// <summary>Calculate end point of leg 2 (same as leg 1 start)</summary>
    private Vec2 CalculateLeg2End()
    {
        return CalculateLeg1Start();
    }

    /// <summary>Normalize heading to 0-360 range</summary>
    private static double NormalizeHeading(double heading)
    {
        return (heading % 360 + 360) % 360;
    }

    /// <summary>Calculate shortest heading difference (-180 to +180)</summary>
    private static double NormalizeHeadingDiff(double diff)
    {
        diff = (diff % 360 + 360) % 360;
        if (diff > 180) diff -= 360;
        return diff;
    }
}
