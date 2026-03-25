using DEADSKY.Core.Physics;

namespace DEADSKY.Core.Entities;

public enum GuidanceMode
{
    SemiActiveRadar,    // Radar illuminates, missile homes on reflection
    ActiveRadar,        // Missile has own seeker (fire and forget)
    CommandGuidance,    // Ground controller sends steering commands
    Ballistic           // Guidance lost — coasting
}

public enum MissilePhase
{
    Boost,      // Motor burning, accelerating
    Sustain,    // Sustainer motor burning
    Coast,      // Motor burned out, coasting
    Terminal    // Final approach
}

/// <summary>
/// A SAM missile in flight. Uses proportional navigation to intercept target.
/// Fuel-limited, can lose guidance, can be decoyed.
/// </summary>
public class SAMMissile : Entity
{
    // ── Configuration ─────────────────────────────────────────────────
    public string MissileTypeName { get; init; } = "9M38";
    public GuidanceMode Guidance { get; set; } = GuidanceMode.SemiActiveRadar;
    public double MaxFlightTimeSec { get; init; } = 30.0;
    public double WarheadRadiusM { get; init; } = 17.0;    // lethal radius meters
    public double FuzeRadiusM { get; init; } = 25.0;       // proximity fuze trigger radius
    public double SingleShotPk { get; init; } = 0.70;

    // ── State ──────────────────────────────────────────────────────────
    public string TargetEntityId { get; set; } = "";
    public MissilePhase Phase { get; set; } = MissilePhase.Boost;
    public double FlightTimeSec { get; set; }
    public bool GuidanceActive { get; set; } = true;
    public string? LaunchedByBatteryId { get; set; }
    public string LauncherId { get; set; } = "L1";

    // ── Detonation ─────────────────────────────────────────────────────
    public bool HasDetonated { get; private set; }
    public bool WasKill { get; private set; }
    public Vec2? DetonationPosition { get; private set; }
    public bool DetonationProcessed { get; set; }

    public SAMMissile()
    {
        Type = EntityType.SAMMissile;
        Affiliation = Affiliation.Friendly;
        FlightModel = FlightModel.SAMMissile;
        RcsM2 = 0.05;
    }

    public override void Update(double deltaTime)
    {
        if (!IsActive || HasDetonated) return;

        FlightTimeSec += deltaTime;

        if (FlightTimeSec > MaxFlightTimeSec)
        {
            // Missile self-destructs — timed fuze
            Detonate(false, Position);
            return;
        }

        base.Update(deltaTime);
    }

    /// <summary>
    /// Update missile guidance toward target.
    /// Call this each tick BEFORE base.Update() to set heading.
    /// </summary>
    public void UpdateGuidance(Entity? target)
    {
        if (target == null || !GuidanceActive || HasDetonated) return;

        Vec2 targetVel = new(target.VelocityX, target.VelocityY);
        double missileGuideSpeed = Math.Max(SpeedMps, FlightModel.MaxSpeedMps * 0.7);
        double remainingFlightTime = Math.Max(1.0, MaxFlightTimeSec - FlightTimeSec);
        Vec2 interceptPoint = MissileKinematics.PredictIntercept(
                Position,
                missileGuideSpeed,
                target.Position,
                targetVel,
                remainingFlightTime)
            ?? target.Position;
        double desiredHeading = Position.HeadingTo(interceptPoint);

        // Also try to match target altitude
        RequestedHeadingDeg = desiredHeading;
        RequestedAltitudeM = target.AltitudeM;
        RequestedSpeedMps = FlightModel.MaxSpeedMps;

        // Check proximity fuze
        double distance2D = Position.DistanceTo(target.Position);
        double altDiff = Math.Abs(AltitudeM - target.AltitudeM);
        double distance3D = Math.Sqrt(distance2D * distance2D + altDiff * altDiff);

        if (distance3D <= FuzeRadiusM)
        {
            // DETONATE
            bool kill = MissileKinematics.CalculatePk(distance3D, WarheadRadiusM, target.RcsM2)
                        > SimulationRandom.Instance.NextDouble();
            Detonate(kill, Position);
        }
    }

    public void LoseGuidance()
    {
        GuidanceActive = false;
        Phase = MissilePhase.Coast;
    }

    private void Detonate(bool kill, Vec2 position)
    {
        HasDetonated = true;
        WasKill = kill;
        DetonationPosition = position;
        Status = EntityStatus.MissileSelfDestructed;
    }
}

/// <summary>
/// An incoming cruise missile or ballistic threat.
/// </summary>
public class IncomingMissile : Entity
{
    public string MissileTypeName { get; init; } = "Cruise";
    public Vec2 TargetPosition { get; init; }  // Where it's headed
    public bool HasBeenIntercepted { get; set; }
    public bool ReachedTarget { get; set; }
    public double DistanceToTargetM => Position.DistanceTo(TargetPosition);

    public IncomingMissile()
    {
        Type = EntityType.CruiseMissile;
        Affiliation = Affiliation.Hostile;
        FlightModel = FlightModel.CruiseMissile;
        RcsM2 = 0.1;  // Small radar cross section
    }

    public override void Update(double deltaTime)
    {
        if (!IsActive) return;

        // Steer toward target
        RequestedHeadingDeg = Position.HeadingTo(TargetPosition);
        RequestedAltitudeM = AltitudeM; // Terrain following handled externally

        if (DistanceToTargetM < 200)
        {
            ReachedTarget = true;
            Status = EntityStatus.Destroyed;
        }

        base.Update(deltaTime);
    }
}
