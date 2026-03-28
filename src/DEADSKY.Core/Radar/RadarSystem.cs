using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;

namespace DEADSKY.Core.Radar;

/// <summary>
/// The Radar System orchestrates detection: each tick it checks if the sweep
/// beam is illuminating any entity, calculates Pd, and feeds detections to TrackManager.
/// </summary>
public class RadarSystem
{
    private readonly DetectionEngine _detector = new();
    public TrackManager TrackManager { get; } = new();
    public RadarModel Model { get; set; } = new();

    // Radar state
    public double SweepAngleDeg { get; private set; }
    public RadarMode Mode { get; set; } = RadarMode.Search;
    public string? SingleTargetTrackEntityId { get; set; }  // For STT mode

    // Task 5.4: AWACS data fusion multiplier (1.0 = no boost, 1.3 = 30% range boost)
    // Requirement 4.4: When AWACS is active, apply 30% radar range multiplier.
    // Requirement 4.5: AWACS must be a distinct airborne entity to provide the boost.
    public double AwacRangeMultiplier { get; private set; } = 1.0;

    // ECM state — currently active jamming
    public List<EcmEffect> ActiveEcmEffects { get; } = new();

    public record EcmEffect(
        string SourceEntityId,
        double BearingDeg,
        double PowerW,
        double StrengthNormalized); // 0-1

    // ── Update ────────────────────────────────────────────────────────

    public void Update(
        double deltaTime,
        IReadOnlyList<Entity> entities,
        double weatherPrecipitationMmHr = 0,
        SAMBattery? battery = null)
    {
        if (!Model.IsOnline || Mode is RadarMode.Silent or RadarMode.Standby) return;

        // Update sweep angle
        double sweepRateDeg = Mode == RadarMode.SingleTargetTrack ? 0.0 : Model.SweepRateDegSec;
        SweepAngleDeg = (SweepAngleDeg + sweepRateDeg * deltaTime) % 360.0;

        // In STT mode, beam is locked on one entity
        if (Mode == RadarMode.SingleTargetTrack && SingleTargetTrackEntityId != null)
        {
            var targetEntity = entities.FirstOrDefault(e => e.Id == SingleTargetTrackEntityId);
            if (targetEntity != null)
            {
                SweepAngleDeg = CoordinateSystem.ToBearingRange(targetEntity.Position).bearingDeg;
            }
        }

        // Clear old ECM effects
        ActiveEcmEffects.Clear();

        double weatherAtten = DetectionEngine.GetWeatherAttenuation(weatherPrecipitationMmHr);

        // Task 5.4: Check for active AWACS entity and set range multiplier.
        // Requirement 4.4: Apply 30% radar range boost when AWACS is on station.
        // Requirement 4.5: AWACS must be a distinct airborne friendly entity.
        bool awacsActive = entities.Any(e =>
            e is AWACSAircraft && e.IsActive && e.Affiliation == Affiliation.Friendly);
        AwacRangeMultiplier = awacsActive ? 1.3 : 1.0;

        // Process all entities
        foreach (var entity in entities)
        {
            if (!entity.IsActive) continue;
            if (entity.Type == EntityType.SAMBattery) continue; // Don't track own battery
            if (entity.Type == EntityType.SAMMissile && entity.Affiliation == Affiliation.Friendly) continue;

            // Check if this entity is jamming
            if (entity is Aircraft aircraft && aircraft.ECMActive && aircraft.EcmPower > 0)
            {
                var (brg, rng) = CoordinateSystem.ToBearingRange(entity.Position);
                ActiveEcmEffects.Add(new EcmEffect(
                    entity.Id, brg, aircraft.EcmPower,
                    Math.Min(1.0, aircraft.EcmPower / 50000.0)));
            }

            // Calculate ECM power from this entity's direction
            double ecmPower = 0;
            if (entity is Aircraft a && a.ECMActive)
                ecmPower = a.EcmPower;

            var (bearing, rangeNm) = CoordinateSystem.ToBearingRange(entity.Position);

            // Task 5.4: Apply AWACS range multiplier — divide effective range so detection
            // probability is calculated as if the target is closer (= extended detection range).
            double effectiveRangeNm = rangeNm / AwacRangeMultiplier;

            var ctx = new DetectionEngine.DetectionContext
            {
                RangeNm = effectiveRangeNm,
                TargetAltFt = CoordinateSystem.MToFt(entity.AltitudeM),
                TargetRcsM2 = entity.RcsM2,
                TargetECMActive = ecmPower > 0,
                EcmPowerW = ecmPower,
                WeatherAttenuationDbKm = weatherAtten,
                SweepAngleDeg = SweepAngleDeg,
                TargetBearingDeg = bearing,
                BeamWidthDeg = Model.BeamWidthDeg
            };

            double pd = _detector.CalculatePd(Model, ctx);

            if (pd <= 0) continue;

            // Roll for detection
            if (SimulationRandom.Instance.NextDouble() < pd)
            {
                // IFF — friendly aircraft respond automatically
                bool iffResponse = entity.Affiliation == Affiliation.Friendly ||
                                   entity.Affiliation == Affiliation.Civilian ||
                                   entity.Affiliation == Affiliation.Neutral;

                TrackManager.ProcessDetection(entity, SweepAngleDeg, iffResponse);
            }
        }

        // Update existing tracks (coast non-detected ones, drop old ones)
        TrackManager.Update(deltaTime);

        // Sync battery sweep angle
        if (battery != null)
            battery.RadarSweepAngle = SweepAngleDeg;
    }

    // ── Mode control ──────────────────────────────────────────────────

    public void SetMode(RadarMode mode, string? sttEntityId = null)
    {
        Mode = mode;
        Model.IsOnline = mode is not RadarMode.Silent and not RadarMode.Standby;
        if (mode == RadarMode.SingleTargetTrack)
            SingleTargetTrackEntityId = sttEntityId;
        else if (mode != RadarMode.SingleTargetTrack)
            SingleTargetTrackEntityId = null;
    }

    public void SetRange(double rangeNm)
    {
        Model.MaxRangeNm = rangeNm;
    }
}
