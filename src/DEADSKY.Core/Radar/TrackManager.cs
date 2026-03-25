using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;

namespace DEADSKY.Core.Radar;

public enum TrackClassification
{
    Unknown,
    AssumedHostile,
    Hostile,
    Friendly,
    Neutral,
    Civilian
}

public enum TrackQuality
{
    Firm,       // High confidence, recent contacts
    Fading,     // No recent contact, coasting on predicted
    Lost        // No contact for too long, track dropping
}

/// <summary>
/// A track file represents what the radar system knows about ONE contact.
/// This is NOT the entity itself — it's the radar picture of the entity.
/// Tracks can have inaccuracy, uncertainty, ghost contacts, and be lost.
/// </summary>
public class TrackFile
{
    // ── Identity ──────────────────────────────────────────────────────
    public string TrackId { get; init; } = "";       // "TRK-0001"
    public string? EntityId { get; set; }             // Actual entity ID (null = ghost)
    public string TrackDesignation { get; set; } = "UNKNOWN"; // Best guess aircraft type

    // ── Classification ─────────────────────────────────────────────────
    public TrackClassification Classification { get; set; } = TrackClassification.Unknown;
    public double ClassificationConfidence { get; set; } // 0-1

    // ── Position (what radar THINKS the entity is) ────────────────────
    public Vec2 Position { get; set; }          // Last known position (with noise added)
    public double AltitudeM { get; set; }
    public double HeadingDeg { get; set; }
    public double SpeedMps { get; set; }
    public Vec2 Velocity { get; set; }          // Estimated velocity

    // Position uncertainty (gets larger as track ages without contact)
    public double PositionUncertaintyM { get; set; } = 500;

    // ── Track history ─────────────────────────────────────────────────
    public record TrackHistoryPoint(Vec2 Position, double AltitudeM, DateTime Time);
    public List<TrackHistoryPoint> History { get; } = new();
    private const int MaxHistory = 20;

    // ── Track state ───────────────────────────────────────────────────
    public TrackQuality Quality { get; set; } = TrackQuality.Firm;
    public int DetectionCount { get; set; }     // Total number of detections
    public DateTime LastDetectionTime { get; set; } = DateTime.UtcNow;
    public DateTime TrackInitiatedTime { get; set; } = DateTime.UtcNow;
    public double TimeSinceLastDetectionSec =>
        (DateTime.UtcNow - LastDetectionTime).TotalSeconds;

    // ── IFF state ─────────────────────────────────────────────────────
    public bool IFFInterrogated { get; set; }
    public bool IFFResponse { get; set; }   // Did it respond?
    public DateTime? IFFTime { get; set; }

    // ── Engagement state ──────────────────────────────────────────────
    public bool IsDesignated { get; set; }   // Player has locked this track
    public bool IsBeingEngaged { get; set; } // SAM in flight to this track
    public string? AssignedMissileId { get; set; }

    // ── Threat assessment ─────────────────────────────────────────────
    public double ThreatLevel { get; set; } // 0-1, calculated by ThreatAssessor
    public double TimeToThreatSec { get; set; } // Estimated time to reach battery engagement zone
    public double ClosingSpeedMps { get; set; }  // Positive = closing

    // ── Derived ───────────────────────────────────────────────────────
    public double RangeNm => CoordinateSystem.MetersToNm(Position.Length);
    public double BearingDeg => CoordinateSystem.ToBearingRange(Position).bearingDeg;
    public double AltitudeFt => CoordinateSystem.MToFt(AltitudeM);
    public double SpeedKts => CoordinateSystem.MpsToKts(SpeedMps);
    public bool IsHot => ClosingSpeedMps > 0; // Closing on battery
    public string AspectString => IsHot ? "HOT" : "COLD";

    /// <summary>Update track with new sensor measurement (with noise applied)</summary>
    public void UpdateWithDetection(Vec2 truePosition, double trueAlt, double trueHeading, double trueSpeed, double radarNoise)
    {
        var rng = SimulationRandom.Instance;

        // Apply measurement noise to simulate radar imprecision
        double noiseM = radarNoise * (1.0 + DetectionCount < 3 ? 2.0 : 1.0); // Noisier early
        Vec2 noisyPos = new(
            truePosition.X + (rng.NextDouble() * 2 - 1) * noiseM,
            truePosition.Y + (rng.NextDouble() * 2 - 1) * noiseM);

        // Update estimated velocity from position delta
        if (History.Count > 0)
        {
            double dt = (DateTime.UtcNow - History[^1].Time).TotalSeconds;
            if (dt > 0.1)
            {
                Vec2 posDelta = noisyPos - History[^1].Position;
                Velocity = posDelta / dt;
                HeadingDeg = (Math.Atan2(Velocity.X, Velocity.Y) * 180.0 / Math.PI + 360) % 360;
                SpeedMps = Velocity.Length;
            }
        }

        Position = noisyPos;
        AltitudeM = trueAlt + (rng.NextDouble() * 2 - 1) * 50; // ±50m altitude noise
        PositionUncertaintyM = noiseM;
        LastDetectionTime = DateTime.UtcNow;
        DetectionCount++;
        Quality = TrackQuality.Firm;

        History.Add(new TrackHistoryPoint(noisyPos, AltitudeM, DateTime.UtcNow));
        if (History.Count > MaxHistory)
            History.RemoveAt(0);
    }

    /// <summary>Coast the track (no detection this sweep) — propagate using estimated velocity</summary>
    public void Coast(double deltaTime)
    {
        Position = Position + Velocity * deltaTime;
        AltitudeM += 0; // Maintain altitude estimate
        PositionUncertaintyM += 200 * deltaTime; // Uncertainty grows with time

        double ageSec = TimeSinceLastDetectionSec;
        Quality = ageSec switch
        {
            < 15 => TrackQuality.Firm,
            < 45 => TrackQuality.Fading,
            _ => TrackQuality.Lost
        };
    }

    public void UpdateThreatAssessment()
    {
        // Calculate closing speed toward origin (battery)
        double range = Position.Length;
        if (range < 1) { ClosingSpeedMps = 0; return; }

        // Component of velocity toward origin
        Vec2 toOrigin = (Vec2.Zero - Position).Normalized();
        ClosingSpeedMps = Velocity.Dot(toOrigin);

        // Time to engagement envelope (18nm default)
        double engagementRangeM = CoordinateSystem.NmToMeters(18);
        if (ClosingSpeedMps > 0 && range > engagementRangeM)
            TimeToThreatSec = (range - engagementRangeM) / ClosingSpeedMps;
        else
            TimeToThreatSec = ClosingSpeedMps > 0 ? 0 : double.MaxValue;
    }
}

/// <summary>
/// Manages all track files. Creates tracks on new detections,
/// correlates repeat detections to existing tracks, drops stale tracks.
/// </summary>
public class TrackManager
{
    private readonly Dictionary<string, TrackFile> _tracks = new();
    private int _nextTrackNumber = 1;
    private const double CorrelationRadiusM = 5000;   // 5km - same entity if within this
    private const double TrackDropTimeSec = 60.0;     // Drop track after 60s no detection
    private const double RadarPositionNoise = 300.0;  // meters of radar noise

    public event Action<TrackFile>? TrackInitiated;
    public event Action<TrackFile>? TrackUpdated;
    public event Action<TrackFile>? TrackDropped;

    // ── Main processing ────────────────────────────────────────────────

    /// <summary>
    /// Process a new detection and either create a new track or update an existing one.
    /// Called by DetectionEngine for each entity that detects this sweep.
    /// </summary>
    public TrackFile ProcessDetection(
        Entity entity, double sweepAngle,
        bool iffResponse, double radarNoise = RadarPositionNoise)
    {
        // Try to correlate with existing track
        var existingTrack = FindCorrelatedTrack(entity.Position);

        if (existingTrack != null)
        {
            // Update existing track
            existingTrack.EntityId = entity.Id;
            existingTrack.UpdateWithDetection(
                entity.Position, entity.AltitudeM,
                entity.HeadingDeg, entity.SpeedMps, radarNoise);

            if (iffResponse && !existingTrack.IFFInterrogated)
            {
                existingTrack.IFFInterrogated = true;
                existingTrack.IFFResponse = true;
                existingTrack.IFFTime = DateTime.UtcNow;
                existingTrack.Classification = entity.Affiliation switch
                {
                    Affiliation.Hostile => TrackClassification.Hostile,
                    Affiliation.Friendly => TrackClassification.Friendly,
                    Affiliation.Civilian => TrackClassification.Civilian,
                    Affiliation.Neutral => TrackClassification.Neutral,
                    _ => TrackClassification.Unknown
                };
            }

            existingTrack.UpdateThreatAssessment();
            TrackUpdated?.Invoke(existingTrack);
            return existingTrack;
        }
        else
        {
            // Create new track
            var track = new TrackFile
            {
                TrackId = $"TRK-{_nextTrackNumber:D4}",
                EntityId = entity.Id,
                TrackDesignation = GuessDesignation(entity),
                Classification = TrackClassification.Unknown,
                TrackInitiatedTime = DateTime.UtcNow
            };
            _nextTrackNumber++;

            track.UpdateWithDetection(
                entity.Position, entity.AltitudeM,
                entity.HeadingDeg, entity.SpeedMps, radarNoise);

            if (iffResponse)
            {
                track.IFFInterrogated = true;
                track.IFFResponse = true;
                track.Classification = entity.Affiliation switch
                {
                    Affiliation.Friendly => TrackClassification.Friendly,
                    Affiliation.Civilian => TrackClassification.Civilian,
                    Affiliation.Neutral => TrackClassification.Neutral,
                    _ => TrackClassification.AssumedHostile
                };
            }

            track.UpdateThreatAssessment();
            _tracks[track.TrackId] = track;
            TrackInitiated?.Invoke(track);
            return track;
        }
    }

    /// <summary>
    /// Update all tracks: coast non-detected tracks, drop stale ones.
    /// Called every simulation tick.
    /// </summary>
    public void Update(double deltaTime)
    {
        var toRemove = new List<string>();

        foreach (var track in _tracks.Values)
        {
            track.Coast(deltaTime);
            track.UpdateThreatAssessment();

            if (track.TimeSinceLastDetectionSec > TrackDropTimeSec)
                toRemove.Add(track.TrackId);
        }

        foreach (var id in toRemove)
        {
            TrackDropped?.Invoke(_tracks[id]);
            _tracks.Remove(id);
        }
    }

    // ── Queries ───────────────────────────────────────────────────────

    public IReadOnlyList<TrackFile> GetAllTracks() => _tracks.Values.ToList();

    public IReadOnlyList<TrackFile> GetFirmTracks() =>
        _tracks.Values.Where(t => t.Quality != TrackQuality.Lost).ToList();

    public IReadOnlyList<TrackFile> GetHostileTracks() =>
        _tracks.Values.Where(t =>
            t.Classification == TrackClassification.Hostile ||
            t.Classification == TrackClassification.AssumedHostile).ToList();

    public TrackFile? GetByEntityId(string entityId) =>
        _tracks.Values.FirstOrDefault(t => t.EntityId == entityId);

    public TrackFile? GetById(string trackId) =>
        _tracks.TryGetValue(trackId, out var t) ? t : null;

    public TrackFile? GetDesignatedTrack() =>
        _tracks.Values.FirstOrDefault(t => t.IsDesignated);

    public int TrackCount => _tracks.Count;

    public void DesignateTrack(string trackId)
    {
        foreach (var t in _tracks.Values) t.IsDesignated = false;
        if (_tracks.TryGetValue(trackId, out var track))
            track.IsDesignated = true;
    }

    public void DropTrackForEntity(string entityId)
    {
        var track = GetByEntityId(entityId);
        if (track != null)
        {
            TrackDropped?.Invoke(track);
            _tracks.Remove(track.TrackId);
        }
    }

    public void Clear() => _tracks.Clear();

    // ── Private helpers ───────────────────────────────────────────────

    private TrackFile? FindCorrelatedTrack(Vec2 newPosition)
    {
        TrackFile? best = null;
        double bestDist = CorrelationRadiusM;

        foreach (var track in _tracks.Values)
        {
            double dist = track.Position.DistanceTo(newPosition);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = track;
            }
        }
        return best;
    }

    private static string GuessDesignation(Entity entity) =>
        entity switch
        {
            Aircraft a => a.Designation,
            IncomingMissile m => m.MissileTypeName,
            _ => "UNKNOWN"
        };
}
