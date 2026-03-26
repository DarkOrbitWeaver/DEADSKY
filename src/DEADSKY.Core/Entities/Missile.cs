using DEADSKY.Core.Physics;

namespace DEADSKY.Core.Entities;

public enum GuidanceMode
{
    SemiActiveRadar,
    ActiveRadar,
    Infrared,
    CommandGuidance,
    Ballistic
}

public enum MissilePhase
{
    Boost,
    Sustain,
    Coast,
    Terminal
}

/// <summary>
/// A SAM missile in flight. Uses proportional navigation to intercept target.
/// Fuel-limited, can lose guidance, can be decoyed, and some weapons support abort.
/// </summary>
public class SAMMissile : Entity
{
    public string MissileTypeName { get; init; } = "9M38";
    public string WeaponId { get; init; } = "";
    public GuidanceMode Guidance { get; set; } = GuidanceMode.SemiActiveRadar;
    public double MaxFlightTimeSec { get; init; } = 30.0;
    public double WarheadRadiusM { get; init; } = 17.0;
    public double FuzeRadiusM { get; init; } = 25.0;
    public double SingleShotPk { get; init; } = 0.70;
    public double GuidanceMemorySec { get; init; } = 2.8;
    public bool CanAbortInFlight { get; init; }
    public bool SusceptibleToChaff { get; init; }
    public bool SusceptibleToFlares { get; init; }

    public string TargetEntityId { get; set; } = "";
    public MissilePhase Phase { get; set; } = MissilePhase.Boost;
    public double FlightTimeSec { get; set; }
    public bool GuidanceActive { get; set; } = true;
    public string? LaunchedByBatteryId { get; set; }
    public string LauncherId { get; set; } = "L1";
    public double GuidanceMemoryRemainingSec { get; private set; }
    public Vec2? LastKnownTargetPosition { get; private set; }
    public Vec2 LastKnownTargetVelocity { get; private set; }
    public double LastKnownTargetAltitudeM { get; private set; }
    public double ClosestApproachM { get; private set; } = double.MaxValue;

    public bool HasDetonated { get; private set; }
    public bool WasKill { get; private set; }
    public Vec2? DetonationPosition { get; private set; }
    public bool DetonationProcessed { get; set; }
    public bool AbortRequested { get; private set; }
    public bool CountermeasureDecoyed { get; private set; }

    public SAMMissile()
    {
        Type = EntityType.SAMMissile;
        Affiliation = Affiliation.Friendly;
        FlightModel = FlightModel.SAMMissile;
        RcsM2 = 0.05;
    }

    public override void Update(double deltaTime)
    {
        if (!IsActive || HasDetonated)
            return;

        FlightTimeSec += deltaTime;

        if (FlightTimeSec > MaxFlightTimeSec)
        {
            Detonate(false, Position);
            return;
        }

        base.Update(deltaTime);
    }

    public void UpdateGuidance(Entity? target)
    {
        if (target == null || !GuidanceActive || HasDetonated)
            return;

        if (target is Aircraft aircraft && TryBreakLockFromCountermeasures(aircraft))
        {
            LoseGuidance();
            return;
        }

        Vec2 targetVel = new(target.VelocityX, target.VelocityY);
        LastKnownTargetPosition = target.Position;
        LastKnownTargetVelocity = targetVel;
        LastKnownTargetAltitudeM = target.AltitudeM;
        GuidanceMemoryRemainingSec = GuidanceMemorySec;

        UpdateGuidanceSolution(target.Position, targetVel, target.AltitudeM);
        EvaluateTerminalGeometry(target.Position, target.AltitudeM, target.RcsM2);
    }

    public void UpdateGuidanceFromMemory(double deltaTime)
    {
        if (HasDetonated || !LastKnownTargetPosition.HasValue)
        {
            LoseGuidance();
            return;
        }

        GuidanceMemoryRemainingSec -= deltaTime;
        if (GuidanceMemoryRemainingSec <= 0)
        {
            LoseGuidance();
            return;
        }

        LastKnownTargetPosition = LastKnownTargetPosition.Value + (LastKnownTargetVelocity * deltaTime);
        UpdateGuidanceSolution(LastKnownTargetPosition.Value, LastKnownTargetVelocity, LastKnownTargetAltitudeM);
        EvaluateTerminalGeometry(LastKnownTargetPosition.Value, LastKnownTargetAltitudeM, targetRcsM2: 5.0);
    }

    private void UpdateGuidanceSolution(Vec2 targetPosition, Vec2 targetVel, double targetAltitudeM)
    {
        if (HasDetonated)
            return;

        double missileGuideSpeed = Math.Max(SpeedMps, FlightModel.MaxSpeedMps * 0.7);
        double remainingFlightTime = Math.Max(1.0, MaxFlightTimeSec - FlightTimeSec);
        Vec2 interceptPoint = MissileKinematics.PredictIntercept(
                Position,
                missileGuideSpeed,
                targetPosition,
                targetVel,
                remainingFlightTime)
            ?? targetPosition;
        double desiredHeading = Position.HeadingTo(interceptPoint);

        RequestedHeadingDeg = desiredHeading;
        RequestedAltitudeM = targetAltitudeM;
        RequestedSpeedMps = FlightModel.MaxSpeedMps;
    }

    private void EvaluateTerminalGeometry(Vec2 targetPosition, double targetAltitudeM, double targetRcsM2)
    {
        if (HasDetonated)
            return;

        double distance2D = Position.DistanceTo(targetPosition);
        double altDiff = Math.Abs(AltitudeM - targetAltitudeM);
        double distance3D = Math.Sqrt(distance2D * distance2D + altDiff * altDiff);
        ClosestApproachM = Math.Min(ClosestApproachM, distance3D);

        if (distance3D <= FuzeRadiusM)
        {
            bool kill = MissileKinematics.CalculatePk(distance3D, WarheadRadiusM, targetRcsM2)
                        > SimulationRandom.Instance.NextDouble();
            Detonate(kill, Position);
            return;
        }

        double desiredBearing = Position.HeadingTo(targetPosition);
        double headingError = Math.Abs(FlightModel.NormalizeHeadingDiff(desiredBearing - HeadingDeg));
        bool targetIsBehind = headingError > 120;
        bool passedClosestApproach = distance3D > ClosestApproachM + 120;

        if (FlightTimeSec > 1.5 && targetIsBehind && passedClosestApproach)
            Detonate(false, Position);
    }

    public void LoseGuidance()
    {
        GuidanceActive = false;
        Phase = MissilePhase.Coast;
    }

    public bool TryAbort()
    {
        if (!CanAbortInFlight || HasDetonated)
            return false;

        AbortRequested = true;
        Detonate(false, Position);
        return true;
    }

    private bool TryBreakLockFromCountermeasures(Aircraft aircraft)
    {
        bool chaffBreak = SusceptibleToChaff && aircraft.IsChaffActive && SimulationRandom.Instance.NextDouble() < 0.35;
        bool flareBreak = SusceptibleToFlares && aircraft.IsFlareActive && SimulationRandom.Instance.NextDouble() < 0.45;
        if (!chaffBreak && !flareBreak)
            return false;

        CountermeasureDecoyed = true;
        return true;
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
    public Vec2 TargetPosition { get; init; }
    public bool HasBeenIntercepted { get; set; }
    public bool ReachedTarget { get; set; }
    public double DistanceToTargetM => Position.DistanceTo(TargetPosition);

    public IncomingMissile()
    {
        Type = EntityType.CruiseMissile;
        Affiliation = Affiliation.Hostile;
        FlightModel = FlightModel.CruiseMissile;
        RcsM2 = 0.1;
    }

    public override void Update(double deltaTime)
    {
        if (!IsActive)
            return;

        RequestedHeadingDeg = Position.HeadingTo(TargetPosition);
        RequestedAltitudeM = AltitudeM;

        if (DistanceToTargetM < 200)
        {
            ReachedTarget = true;
            Status = EntityStatus.Destroyed;
        }

        base.Update(deltaTime);
    }
}
