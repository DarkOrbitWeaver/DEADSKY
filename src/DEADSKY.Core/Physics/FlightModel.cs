namespace DEADSKY.Core.Physics;

/// <summary>
/// Aircraft flight model. All speeds in m/s, altitudes in meters, headings in degrees.
/// Turn rates, climb rates, and acceleration are enforced per aircraft type.
/// </summary>
public class FlightModel
{
    // Max turn rate in deg/s at cruise speed (degrades at high speed)
    public double MaxTurnRateDegSec { get; init; } = 6.0;
    // Max climb rate m/s
    public double MaxClimbRateMps { get; init; } = 80.0;
    // Max descent rate m/s
    public double MaxDescentRateMps { get; init; } = 100.0;
    // Acceleration m/s²
    public double MaxAccelMps2 { get; init; } = 10.0;
    // Deceleration m/s²
    public double MaxDecelMps2 { get; init; } = 15.0;
    // Min/max airspeed m/s
    public double MinSpeedMps { get; init; } = 50.0;
    public double MaxSpeedMps { get; init; } = 350.0;

    /// <summary>
    /// Update position, velocity, heading and altitude for one simulation tick.
    /// requestedHeading: target heading (degrees)
    /// requestedAltM: target altitude (meters)
    /// requestedSpeedMps: target speed (m/s)
    /// </summary>
    public void Update(
        ref Vec2 position,
        ref double velocityX, ref double velocityY,
        ref double headingDeg,
        ref double altitudeM,
        ref double speedMps,
        double requestedHeadingDeg,
        double requestedAltitudeM,
        double requestedSpeedMps,
        double deltaTime)
    {
        // ── Speed update ─────────────────────────────────────────────
        double targetSpeed = Math.Clamp(requestedSpeedMps, MinSpeedMps, MaxSpeedMps);
        double speedDiff = targetSpeed - speedMps;
        double speedChange = speedDiff > 0
            ? Math.Min(MaxAccelMps2 * deltaTime, speedDiff)
            : Math.Max(-MaxDecelMps2 * deltaTime, speedDiff);
        speedMps += speedChange;
        speedMps = Math.Clamp(speedMps, MinSpeedMps, MaxSpeedMps);

        // ── Heading update ────────────────────────────────────────────
        double headingDiff = NormalizeHeadingDiff(requestedHeadingDeg - headingDeg);
        // Turn rate degrades slightly at high speed (realistic)
        double speedFactor = Math.Max(0.3, 1.0 - (speedMps / MaxSpeedMps) * 0.4);
        double effectiveTurnRate = MaxTurnRateDegSec * speedFactor;
        double maxTurnThisTick = effectiveTurnRate * deltaTime;
        double headingChange = Math.Clamp(headingDiff, -maxTurnThisTick, maxTurnThisTick);
        headingDeg = (headingDeg + headingChange + 360.0) % 360.0;

        // ── Altitude update ───────────────────────────────────────────
        double altDiff = requestedAltitudeM - altitudeM;
        double climbRate = altDiff > 0
            ? Math.Min(MaxClimbRateMps * deltaTime, altDiff)
            : Math.Max(-MaxDescentRateMps * deltaTime, altDiff);
        altitudeM += climbRate;
        altitudeM = Math.Max(0.0, altitudeM);

        // ── Position update ───────────────────────────────────────────
        double headingRad = headingDeg * Math.PI / 180.0;
        velocityX = Math.Sin(headingRad) * speedMps;
        velocityY = Math.Cos(headingRad) * speedMps;
        position = new Vec2(
            position.X + velocityX * deltaTime,
            position.Y + velocityY * deltaTime
        );
    }

    /// <summary>Returns -180 to +180 heading difference</summary>
    public static double NormalizeHeadingDiff(double diff)
    {
        diff = diff % 360.0;
        if (diff > 180.0) diff -= 360.0;
        if (diff < -180.0) diff += 360.0;
        return diff;
    }

    // ── Preset flight models for each aircraft type ───────────────────

    public static FlightModel Fighter => new()
    {
        MaxTurnRateDegSec = 8.0,
        MaxClimbRateMps = 120.0,
        MaxDescentRateMps = 150.0,
        MaxAccelMps2 = 18.0,
        MaxDecelMps2 = 25.0,
        MinSpeedMps = 80.0,
        MaxSpeedMps = 680.0   // ~Mach 2
    };

    public static FlightModel Bomber => new()
    {
        MaxTurnRateDegSec = 3.0,
        MaxClimbRateMps = 40.0,
        MaxDescentRateMps = 60.0,
        MaxAccelMps2 = 8.0,
        MaxDecelMps2 = 12.0,
        MinSpeedMps = 100.0,
        MaxSpeedMps = 400.0
    };

    public static FlightModel CruiseMissile => new()
    {
        MaxTurnRateDegSec = 12.0,
        MaxClimbRateMps = 30.0,
        MaxDescentRateMps = 50.0,
        MaxAccelMps2 = 5.0,
        MaxDecelMps2 = 5.0,
        MinSpeedMps = 200.0,
        MaxSpeedMps = 280.0
    };

    public static FlightModel Drone => new()
    {
        MaxTurnRateDegSec = 10.0,
        MaxClimbRateMps = 20.0,
        MaxDescentRateMps = 30.0,
        MaxAccelMps2 = 8.0,
        MaxDecelMps2 = 10.0,
        MinSpeedMps = 20.0,
        MaxSpeedMps = 120.0
    };

    public static FlightModel SAMMissile => new()
    {
        MaxTurnRateDegSec = 80.0,
        MaxClimbRateMps = 500.0,
        MaxDescentRateMps = 500.0,
        MaxAccelMps2 = 400.0,
        MaxDecelMps2 = 400.0,
        MinSpeedMps = 100.0,
        MaxSpeedMps = 1200.0  // Mach 3.5
    };
}

/// <summary>
/// Missile kinematics — proportional navigation guidance.
/// Steers the missile to an intercept point, not directly at the target.
/// </summary>
public static class MissileKinematics
{
    private const double NavigationConstant = 4.0; // N' for proportional navigation

    /// <summary>
    /// Proportional navigation: returns the desired heading for the missile to intercept target.
    /// </summary>
    public static double CalculateProNavHeading(
        Vec2 missilePos, double missileSpeedMps,
        Vec2 targetPos, Vec2 targetVelocity,
        double currentMissileHeading)
    {
        Vec2 relPos = targetPos - missilePos;
        double range = relPos.Length;
        if (range < 1.0) return currentMissileHeading;

        // Line-of-sight rate
        Vec2 relVel = targetVelocity - new Vec2(
            Math.Sin(currentMissileHeading * Math.PI / 180.0) * missileSpeedMps,
            Math.Cos(currentMissileHeading * Math.PI / 180.0) * missileSpeedMps);

        double losRateDeg = relPos.Cross(relVel) / (range * range) * 180.0 / Math.PI;

        // Commanded acceleration (lateral)
        double commandedAccel = NavigationConstant * missileSpeedMps * losRateDeg;

        // Convert to heading change
        double headingChangeDeg = commandedAccel > 0
            ? Math.Min(80.0, commandedAccel * 0.1)
            : Math.Max(-80.0, commandedAccel * 0.1);

        return (currentMissileHeading + headingChangeDeg + 360.0) % 360.0;
    }

    /// <summary>
    /// Predict intercept point for a constant-velocity target.
    /// Returns the predicted intercept position, or null if no intercept possible.
    /// </summary>
    public static Vec2? PredictIntercept(
        Vec2 launchPos, double missileSpeedMps,
        Vec2 targetPos, Vec2 targetVelocity,
        double maxFlightTimeSec)
    {
        if (missileSpeedMps <= 1.0)
            return null;

        Vec2 relativePosition = targetPos - launchPos;
        double targetSpeedSq = targetVelocity.Dot(targetVelocity);
        double missileSpeedSq = missileSpeedMps * missileSpeedMps;

        double a = targetSpeedSq - missileSpeedSq;
        double b = 2.0 * relativePosition.Dot(targetVelocity);
        double c = relativePosition.Dot(relativePosition);

        double? interceptTime = SolvePositiveInterceptTime(a, b, c);
        if (!interceptTime.HasValue)
            return null;

        double time = interceptTime.Value;
        if (time <= 0 || time > maxFlightTimeSec)
            return null;

        return targetPos + targetVelocity * time;
    }

    private static double? SolvePositiveInterceptTime(double a, double b, double c)
    {
        const double epsilon = 1e-6;

        if (Math.Abs(a) < epsilon)
        {
            if (Math.Abs(b) < epsilon)
                return null;

            double linearTime = -c / b;
            return linearTime > 0 ? linearTime : null;
        }

        double discriminant = (b * b) - (4.0 * a * c);
        if (discriminant < 0)
            return null;

        double sqrtDiscriminant = Math.Sqrt(discriminant);
        double t1 = (-b - sqrtDiscriminant) / (2.0 * a);
        double t2 = (-b + sqrtDiscriminant) / (2.0 * a);

        double best = double.MaxValue;
        if (t1 > 0)
            best = t1;
        if (t2 > 0 && t2 < best)
            best = t2;

        return best == double.MaxValue ? null : best;
    }

    /// <summary>
    /// Calculate probability of kill based on miss distance, warhead radius, and target size.
    /// </summary>
    public static double CalculatePk(double missDistanceM, double warheadRadiusM, double targetRcsM2)
    {
        if (missDistanceM <= warheadRadiusM * 0.3)
            return 0.98; // Direct hit zone
        if (missDistanceM >= warheadRadiusM * 2.5)
            return 0.0;  // Well outside lethal radius

        double normalized = (missDistanceM - warheadRadiusM * 0.3) / (warheadRadiusM * 2.2);
        double pkBase = Math.Exp(-normalized * normalized * 3.0);

        // Target size modifier (larger target = easier to kill)
        double sizeMod = Math.Sqrt(targetRcsM2 / 5.0); // normalize to fighter-size RCS
        sizeMod = Math.Clamp(sizeMod, 0.3, 2.0);

        return Math.Clamp(pkBase * sizeMod, 0.0, 0.98);
    }
}
