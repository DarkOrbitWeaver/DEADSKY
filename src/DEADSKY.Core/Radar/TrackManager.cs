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
    public string GroupLabel { get; set; } = "UNATTRIBUTED";

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
    internal bool DetectedThisUpdate { get; set; }

    // ── IFF state ─────────────────────────────────────────────────────
    public bool IFFInterrogated { get; set; }
    public bool IFFResponse { get; set; }   // Did it respond?
    public DateTime? IFFTime { get; set; }

    // ── Engagement state ──────────────────────────────────────────────
    public bool IsDesignated { get; set; }   // Player has hard-locked this track
    public bool IsTrackHeld { get; set; }    // Player is keeping this track in TWS memory
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
    public bool HasFireControlAttention => IsDesignated || IsTrackHeld || IsBeingEngaged;

    /// <summary>Update track with new sensor measurement (with noise applied)</summary>
    public void UpdateWithDetection(Vec2 truePosition, double trueAlt, double trueHeading, double trueSpeed, double radarNoise)
    {
        var rng = SimulationRandom.Instance;
        var now = DateTime.UtcNow;

        // Apply measurement noise, but tighten a designated/STT track so the
        // operator sees a stable fire-control picture instead of a vibrating hit.
        double noiseM = radarNoise * (DetectionCount < 2 ? 2.0 : 1.0);
        if (DetectionCount >= 2)
            noiseM *= 0.55;

        if (IsTrackHeld)
            noiseM *= 0.6;

        if (IsDesignated)
            noiseM *= 0.35;
        Vec2 noisyPos = new(
            truePosition.X + (rng.NextDouble() * 2 - 1) * noiseM,
            truePosition.Y + (rng.NextDouble() * 2 - 1) * noiseM);
        double noisyAlt = trueAlt + (rng.NextDouble() * 2 - 1) * 50;

        double positionCorrection = DetectionCount switch
        {
            0 => 1.0,
            1 => 0.58,
            _ when IsDesignated => 0.18,
            _ when IsTrackHeld => 0.24,
            _ => 0.32
        };

        double altitudeCorrection = DetectionCount == 0
            ? 1.0
            : IsDesignated
                ? 0.22
                : IsTrackHeld
                    ? 0.32
                : 0.45;

        Vec2 filteredPos = DetectionCount == 0
            ? noisyPos
            : Position + (noisyPos - Position) * positionCorrection;

        double filteredAlt = DetectionCount == 0
            ? noisyAlt
            : AltitudeM + (noisyAlt - AltitudeM) * altitudeCorrection;

        // Use correlated kinematics to avoid deriving velocity from noisy hits.
        Velocity = Vec2.FromHeading(trueHeading) * trueSpeed;
        HeadingDeg = trueHeading;
        SpeedMps = trueSpeed;

        Position = filteredPos;
        AltitudeM = trueAlt + (rng.NextDouble() * 2 - 1) * 50; // ±50m altitude noise
        AltitudeM = filteredAlt;
        PositionUncertaintyM = Math.Max(75, noiseM * (IsDesignated ? 0.45 : IsTrackHeld ? 0.65 : 0.9));
        LastDetectionTime = now;
        DetectionCount++;
        Quality = TrackQuality.Firm;
        DetectedThisUpdate = true;

        History.Add(new TrackHistoryPoint(noisyPos, AltitudeM, DateTime.UtcNow));
        History[^1] = new TrackHistoryPoint(filteredPos, AltitudeM, now);
        if (History.Count > MaxHistory)
            History.RemoveAt(0);
    }

    /// <summary>Coast the track (no detection this sweep) — propagate using estimated velocity</summary>
    public void Coast(double deltaTime)
    {
        DateTime now = DateTime.UtcNow;
        Position = Position + Velocity * deltaTime;
        AltitudeM += 0; // Maintain altitude estimate
        double uncertaintyGrowth = HasFireControlAttention ? 110 : 200;
        PositionUncertaintyM += uncertaintyGrowth * deltaTime; // Uncertainty grows with time

        double ageSec = TimeSinceLastDetectionSec;
        double firmThreshold = IsDesignated ? 24 : HasFireControlAttention ? 18 : 15;
        double fadingThreshold = IsDesignated ? 90 : HasFireControlAttention ? 75 : 45;
        Quality = ageSec switch
        {
            _ when ageSec < firmThreshold => TrackQuality.Firm,
            _ when ageSec < fadingThreshold => TrackQuality.Fading,
            _ => TrackQuality.Lost
        };

        if (HasFireControlAttention &&
            (History.Count == 0 || (now - History[^1].Time).TotalSeconds >= 1.0))
        {
            History.Add(new TrackHistoryPoint(Position, AltitudeM, now));
            if (History.Count > MaxHistory)
                History.RemoveAt(0);
        }
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

        double rangeThreat = Math.Clamp(1.0 - (range / CoordinateSystem.NmToMeters(65)), 0.0, 1.0);
        double closureThreat = Math.Clamp(ClosingSpeedMps / 320.0, 0.0, 1.0);
        double timeThreat = TimeToThreatSec == double.MaxValue
            ? 0.0
            : Math.Clamp(1.0 - (TimeToThreatSec / 240.0), 0.0, 1.0);
        double classificationThreat = Classification switch
        {
            TrackClassification.Hostile => 1.0,
            TrackClassification.AssumedHostile => 0.8,
            TrackClassification.Unknown => 0.45,
            TrackClassification.Neutral => 0.2,
            _ => 0.0
        };
        double qualityPenalty = Quality switch
        {
            TrackQuality.Firm => 0.0,
            TrackQuality.Fading => -0.08,
            _ => -0.18
        };
        double fireControlBonus = IsBeingEngaged ? 0.08 : IsDesignated ? 0.05 : IsTrackHeld ? 0.03 : 0.0;

        ThreatLevel = Math.Clamp(
            (rangeThreat * 0.32) +
            (closureThreat * 0.22) +
            (timeThreat * 0.26) +
            (classificationThreat * 0.20) +
            qualityPenalty +
            fireControlBonus,
            0.0,
            1.0);
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
    private const double HeldTrackDropTimeSec = 95.0;
    private const double EngagedTrackDropTimeSec = 120.0;
    private const double DesignatedTrackDropTimeSec = 150.0;
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
        var existingTrack = GetByEntityId(entity.Id) ?? FindCorrelatedTrack(entity.Position);

        if (existingTrack != null)
        {
            // Update existing track
            existingTrack.EntityId = entity.Id;
            existingTrack.GroupLabel = GuessGroupLabel(entity);
            existingTrack.UpdateWithDetection(
                entity.Position, entity.AltitudeM,
                entity.HeadingDeg, entity.SpeedMps, radarNoise);

            UpdateClassification(existingTrack, entity, iffResponse);

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
                GroupLabel = GuessGroupLabel(entity),
                Classification = TrackClassification.Unknown,
                TrackInitiatedTime = DateTime.UtcNow
            };
            _nextTrackNumber++;

            track.UpdateWithDetection(
                entity.Position, entity.AltitudeM,
                entity.HeadingDeg, entity.SpeedMps, radarNoise);

            UpdateClassification(track, entity, iffResponse);

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
            if (track.DetectedThisUpdate)
            {
                track.DetectedThisUpdate = false;
            }
            else
            {
                track.Coast(deltaTime);
                track.UpdateThreatAssessment();
            }

            if (track.TimeSinceLastDetectionSec > GetTrackDropTimeSec(track))
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

    public IReadOnlyList<TrackFile> GetHeldTracks() =>
        _tracks.Values.Where(t => t.IsTrackHeld || t.IsDesignated).ToList();

    public int TrackCount => _tracks.Count;
    public int HeldTrackCount => _tracks.Values.Count(t => t.IsTrackHeld || t.IsDesignated);

    public void DesignateTrack(string trackId)
    {
        foreach (var t in _tracks.Values) t.IsDesignated = false;
        if (_tracks.TryGetValue(trackId, out var track))
        {
            track.IsDesignated = true;
            track.IsTrackHeld = true;
        }
    }

    public bool HoldTrack(string trackId, bool held = true)
    {
        if (!_tracks.TryGetValue(trackId, out var track))
            return false;

        track.IsTrackHeld = held;
        if (!held)
            track.IsDesignated = false;

        return true;
    }

    public bool ReleaseTrack(string trackId)
    {
        if (!_tracks.TryGetValue(trackId, out var track))
            return false;

        track.IsTrackHeld = false;
        track.IsDesignated = false;
        return true;
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

    public void Clear()
    {
        _tracks.Clear();
        _nextTrackNumber = 1;
    }

    public void ShiftWallClock(TimeSpan offset)
    {
        if (offset <= TimeSpan.Zero)
            return;

        foreach (var track in _tracks.Values)
        {
            track.LastDetectionTime = track.LastDetectionTime.Add(offset);
            track.TrackInitiatedTime = track.TrackInitiatedTime.Add(offset);
            if (track.IFFTime.HasValue)
                track.IFFTime = track.IFFTime.Value.Add(offset);

            for (int i = 0; i < track.History.Count; i++)
            {
                var point = track.History[i];
                track.History[i] = point with { Time = point.Time.Add(offset) };
            }
        }
    }

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

    private static string GuessGroupLabel(Entity entity) =>
        entity switch
        {
            Aircraft a when !string.IsNullOrWhiteSpace(a.GroupId) => a.GroupId!,
            IncomingMissile => "VAMPIRE TRACK",
            _ => "UNATTRIBUTED"
        };

    private static void UpdateClassification(TrackFile track, Entity entity, bool iffResponse)
    {
        if (iffResponse)
        {
            track.IFFInterrogated = true;
            track.IFFResponse = true;
            track.IFFTime = DateTime.UtcNow;
            track.Classification = entity.Affiliation switch
            {
                Affiliation.Friendly => TrackClassification.Friendly,
                Affiliation.Civilian => TrackClassification.Civilian,
                Affiliation.Neutral => TrackClassification.Neutral,
                Affiliation.Hostile => TrackClassification.Hostile,
                _ => TrackClassification.Unknown
            };
            track.ClassificationConfidence = 1.0;
            return;
        }

        if (entity.Affiliation != Affiliation.Hostile)
        {
            track.ClassificationConfidence = Math.Max(track.ClassificationConfidence, 0.2);
            return;
        }

        track.IFFInterrogated = true;
        track.IFFResponse = false;
        track.IFFTime = DateTime.UtcNow;

        bool immediateHostile = entity is IncomingMissile
            || entity.Type is EntityType.CruiseMissile or EntityType.SAMMissile;

        if (immediateHostile || track.DetectionCount >= 3)
        {
            track.Classification = TrackClassification.Hostile;
            track.ClassificationConfidence = 0.95;
        }
        else
        {
            track.Classification = TrackClassification.AssumedHostile;
            track.ClassificationConfidence = Math.Max(track.ClassificationConfidence, 0.65);
        }
    }

    private static double GetTrackDropTimeSec(TrackFile track)
    {
        if (track.IsDesignated)
            return DesignatedTrackDropTimeSec;

        if (track.IsBeingEngaged)
            return EngagedTrackDropTimeSec;

        if (track.IsTrackHeld)
            return HeldTrackDropTimeSec;

        return TrackDropTimeSec;
    }
}
