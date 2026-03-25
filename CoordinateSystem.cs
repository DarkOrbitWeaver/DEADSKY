namespace DEADSKY.Core.Physics;

/// <summary>
/// All positions are stored internally as meters from a fixed origin point.
/// Conversions to lat/lon and bearing/range happen only at the edges (radar display, map).
/// This is the single source of truth - everything uses Vec2 internally.
/// </summary>
public readonly struct Vec2
{
    public readonly double X; // meters east
    public readonly double Y; // meters north

    public Vec2(double x, double y) { X = x; Y = y; }

    public static Vec2 Zero => new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized()
    {
        double len = Length;
        return len < 1e-10 ? Zero : new Vec2(X / len, Y / len);
    }

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 v, double s) => new(v.X * s, v.Y * s);
    public static Vec2 operator *(double s, Vec2 v) => new(v.X * s, v.Y * s);
    public static Vec2 operator /(Vec2 v, double s) => new(v.X / s, v.Y / s);

    public double Dot(Vec2 other) => X * other.X + Y * other.Y;
    public double Cross(Vec2 other) => X * other.Y - Y * other.X;

    public double DistanceTo(Vec2 other) => (this - other).Length;

    /// <summary>Heading in degrees, 0=North, clockwise</summary>
    public double HeadingTo(Vec2 other)
    {
        Vec2 delta = other - this;
        double rad = Math.Atan2(delta.X, delta.Y); // Note: atan2(east, north) = bearing
        return ((rad * 180.0 / Math.PI) + 360.0) % 360.0;
    }

    /// <summary>Create a unit vector from a heading in degrees</summary>
    public static Vec2 FromHeading(double headingDeg)
    {
        double rad = headingDeg * Math.PI / 180.0;
        return new Vec2(Math.Sin(rad), Math.Cos(rad));
    }

    public override string ToString() => $"({X:F0}m, {Y:F0}m)";
}

/// <summary>
/// Converts between internal meters, geographic lat/lon, and tactical bearing/range.
/// All game logic uses meters. Only output layers (radar, map) convert.
/// </summary>
public static class CoordinateSystem
{
    // Battery ALPHA is at the origin (0,0) in local coordinates
    // Real-world anchor point — Kovran Peninsula (fictional, based loosely on Eastern Med)
    public const double OriginLat = 34.05;
    public const double OriginLon = 35.65;

    private const double MetersPerDegreeLat = 111320.0;
    private const double FeetPerMeter = 3.28084;
    private const double MetersPerNauticalMile = 1852.0;
    private const double KtsToMps = 0.514444; // knots to meters/second
    private const double FtToMeters = 0.3048;

    // ── Unit conversions ───────────────────────────────────────────────

    public static double NmToMeters(double nm) => nm * MetersPerNauticalMile;
    public static double MetersToNm(double m) => m / MetersPerNauticalMile;
    public static double KtsToMps(double kts) => kts * KtsToMps;
    public static double MpsToKts(double mps) => mps / KtsToMps;
    public static double FtToM(double ft) => ft * FtToMeters;
    public static double MToFt(double m) => m / FtToMeters;
    public static double DegToRad(double deg) => deg * Math.PI / 180.0;
    public static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

    // ── Vec2 ↔ Bearing/Range ─────────────────────────────────────────

    /// <summary>Convert meters offset to bearing (degrees) and range (nm) from origin</summary>
    public static (double bearingDeg, double rangeNm) ToBearingRange(Vec2 position)
    {
        double rangeM = position.Length;
        double bearing = rangeM < 0.1 ? 0.0 :
            ((Math.Atan2(position.X, position.Y) * 180.0 / Math.PI) + 360.0) % 360.0;
        return (bearing, MetersToNm(rangeM));
    }

    /// <summary>Convert bearing/range to Vec2 position</summary>
    public static Vec2 FromBearingRange(double bearingDeg, double rangeNm)
    {
        double rangeM = NmToMeters(rangeNm);
        double rad = DegToRad(bearingDeg);
        return new Vec2(Math.Sin(rad) * rangeM, Math.Cos(rad) * rangeM);
    }

    // ── Vec2 ↔ Lat/Lon ────────────────────────────────────────────────

    public static (double lat, double lon) ToLatLon(Vec2 position)
    {
        double metersPerDegreeLon = MetersPerDegreeLat * Math.Cos(DegToRad(OriginLat));
        double lat = OriginLat + position.Y / MetersPerDegreeLat;
        double lon = OriginLon + position.X / metersPerDegreeLon;
        return (lat, lon);
    }

    public static Vec2 FromLatLon(double lat, double lon)
    {
        double metersPerDegreeLon = MetersPerDegreeLat * Math.Cos(DegToRad(OriginLat));
        double y = (lat - OriginLat) * MetersPerDegreeLat;
        double x = (lon - OriginLon) * metersPerDegreeLon;
        return new Vec2(x, y);
    }

    // ── Radar screen coordinates ──────────────────────────────────────

    /// <summary>
    /// Convert world position to radar screen pixel (0,0=center).
    /// radarRangeNm = the current zoom level (e.g. 60nm = full display radius).
    /// displayRadius = pixel radius of the radar circle.
    /// </summary>
    public static (float px, float py) ToRadarScreen(Vec2 worldPos, double radarRangeNm, float displayRadius)
    {
        double rangeM = NmToMeters(radarRangeNm);
        float scale = (float)(displayRadius / rangeM);
        return ((float)worldPos.X * scale, -(float)worldPos.Y * scale); // Y inverted (screen Y down)
    }

    public static Vec2 FromRadarScreen(float px, float py, double radarRangeNm, float displayRadius)
    {
        double rangeM = NmToMeters(radarRangeNm);
        double scale = rangeM / displayRadius;
        return new Vec2(px * scale, -py * scale);
    }
}
