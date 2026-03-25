using DEADSKY.Core.Physics;

namespace DEADSKY.Core.Entities;

public enum EntityType
{
    Aircraft,
    SAMMissile,
    CruiseMissile,
    Drone,
    RadarStation,
    SAMBattery,
    Helicopter,
    Decoy
}

public enum Affiliation
{
    Hostile,
    Friendly,
    Unknown,
    Neutral,
    Civilian
}

public enum EntityStatus
{
    Active,
    Damaged,      // degraded performance
    Destroyed,
    Landed,
    LeftArea,
    MissileSelfDestructed
}

/// <summary>
/// Base class for every simulated object. Stores position in meters (Vec2),
/// velocity in m/s, heading in degrees. All physics happen here.
/// </summary>
public abstract class Entity
{
    // ── Identity ──────────────────────────────────────────────────────
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8].ToUpper();
    public EntityType Type { get; init; }
    public Affiliation Affiliation { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public string Designation { get; init; } = "UNKNOWN"; // "SU-24", "MiG-29", "9M38"
    public string CallSign { get; set; } = "";

    // ── Physics state (internal — meters, m/s, degrees) ───────────────
    public Vec2 Position { get; set; }
    public double VelocityX { get; set; }   // m/s east
    public double VelocityY { get; set; }   // m/s north
    public double HeadingDeg { get; set; }  // 0=North, clockwise
    public double AltitudeM { get; set; }   // meters ASL
    public double SpeedMps { get; set; }    // meters per second

    // ── Requested state (where AI/player wants the entity to go) ──────
    public double RequestedHeadingDeg { get; set; }
    public double RequestedAltitudeM { get; set; }
    public double RequestedSpeedMps { get; set; }

    // ── Physical characteristics ──────────────────────────────────────
    public double RcsM2 { get; init; } = 5.0;          // Radar cross section (m²)
    public FlightModel FlightModel { get; init; } = FlightModel.Fighter;

    // ── State flags ───────────────────────────────────────────────────
    public bool IsBeingEngaged { get; set; }
    public string? EngagedByMissileId { get; set; }
    public bool ECMActive { get; set; }
    public double DamagePct { get; set; }   // 0=undamaged, 1=destroyed
    public DateTime SpawnTime { get; set; } = DateTime.UtcNow;

    // ── Track history (for radar trail) ──────────────────────────────
    public List<(Vec2 pos, double altitude, DateTime time)> PositionHistory { get; } = new();
    private const int MaxHistoryPoints = 30;

    // ── Convenience: derived values ───────────────────────────────────
    public double SpeedKts => CoordinateSystem.MpsToKts(SpeedMps);
    public double AltitudeFt => CoordinateSystem.MToFt(AltitudeM);
    public (double bearing, double range) BearingRange => CoordinateSystem.ToBearingRange(Position);
    public bool IsActive => Status == EntityStatus.Active || Status == EntityStatus.Damaged;

    // ── Update ────────────────────────────────────────────────────────

    public virtual void Update(double deltaTime)
    {
        if (!IsActive) return;

        FlightModel.Update(
            ref _position, ref _vx, ref _vy,
            ref _heading, ref _altitude, ref _speed,
            RequestedHeadingDeg, RequestedAltitudeM, RequestedSpeedMps,
            deltaTime);

        // Apply back to properties
        Position = _position;
        VelocityX = _vx;
        VelocityY = _vy;
        HeadingDeg = _heading;
        AltitudeM = _altitude;
        SpeedMps = _speed;

        // Record position history periodically
        if (PositionHistory.Count == 0 ||
            (DateTime.UtcNow - PositionHistory[^1].time).TotalSeconds > 5.0)
        {
            PositionHistory.Add((Position, AltitudeM, DateTime.UtcNow));
            if (PositionHistory.Count > MaxHistoryPoints)
                PositionHistory.RemoveAt(0);
        }
    }

    // Mutable backing fields for ref passing in FlightModel
    private Vec2 _position;
    private double _vx, _vy, _heading, _altitude, _speed;

    /// <summary>Sync mutable fields from properties (call after setting properties directly)</summary>
    public void SyncPhysicsState()
    {
        _position = Position;
        _vx = VelocityX;
        _vy = VelocityY;
        _heading = HeadingDeg;
        _altitude = AltitudeM;
        _speed = SpeedMps;
        RequestedHeadingDeg = HeadingDeg;
        RequestedAltitudeM = AltitudeM;
        RequestedSpeedMps = SpeedMps;
    }

    public override string ToString() =>
        $"{Designation}[{Id}] {Affiliation} {BearingRange.bearing:F0}°/{BearingRange.range:F1}nm FL{AltitudeFt/100:F0}";
}
