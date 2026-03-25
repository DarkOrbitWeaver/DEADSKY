using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;

namespace DEADSKY.Core.Radar;

public class RadarModel
{
    public double MaxRangeNm { get; set; } = 80.0;
    public double MinAltitudeFt { get; set; } = 500.0;
    public double SweepRateDegSec { get; set; } = 6.0;   // 60 sec full rotation
    public double BeamWidthDeg { get; set; } = 3.0;       // Beam width for detection
    public double ReceiverSensitivityDbm { get; set; } = -110.0;
    public double TransmitPowerDbW { get; set; } = 80.0;  // 100MW
    public double AntennaGainDbi { get; set; } = 32.0;
    public bool IsOnline { get; set; } = true;
    public double HealthPct { get; set; } = 1.0;

    // Upgrades
    public bool HasLowAltModule { get; set; }
    public bool HasECCM { get; set; }

    public double EffectiveMinAltFt => HasLowAltModule ? MinAltitudeFt * 0.35 : MinAltitudeFt;
    public double EffectiveMaxRangeNm => MaxRangeNm * HealthPct;
}

/// <summary>
/// Calculates detection probability for each entity based on:
/// - Range falloff (radar equation: signal ∝ 1/R⁴)
/// - Target RCS
/// - Altitude/terrain masking
/// - ECM jamming
/// - Sweep geometry (is the beam pointing at the target?)
/// - Weather attenuation
/// </summary>
public class DetectionEngine
{
    private const double Pi = Math.PI;

    // Boltzmann constant & system noise
    private const double BoltzmannDbW = -228.6;  // dBW/K/Hz
    private const double SystemNoiseTemp = 500.0;  // Kelvin
    private const double BandwidthHz = 1e6;        // 1 MHz bandwidth

    public struct DetectionContext
    {
        public double RangeNm;
        public double TargetAltFt;
        public double TargetRcsM2;
        public bool TargetECMActive;
        public double EcmPowerW;
        public double WeatherAttenuationDbKm;
        public double SweepAngleDeg;
        public double TargetBearingDeg;
        public double BeamWidthDeg;
    }

    /// <summary>
    /// Returns probability of detection for this scan.
    /// Returns 0 if conditions are impossible (out of range, min alt masked, etc.)
    /// </summary>
    public double CalculatePd(RadarModel radar, DetectionContext ctx)
    {
        if (!radar.IsOnline) return 0.0;

        // ── Geometry check: is beam sweeping past the target? ────────
        double angleDiff = Math.Abs(FlightModel.NormalizeHeadingDiff(ctx.SweepAngleDeg - ctx.TargetBearingDeg));
        if (angleDiff > ctx.BeamWidthDeg / 2.0)
            return 0.0; // Beam not pointing at target this tick

        // ── Range check ───────────────────────────────────────────────
        double maxRange = radar.EffectiveMaxRangeNm;
        if (ctx.RangeNm > maxRange) return 0.0;

        // ── Altitude/terrain mask ─────────────────────────────────────
        double minAlt = radar.EffectiveMinAltFt;
        if (ctx.TargetAltFt < minAlt) return 0.0;

        // ── Radar equation: SNR ───────────────────────────────────────
        // SNR = Pt * G² * λ² * σ / ((4π)³ * R⁴ * k * T * B * Fn)
        // Simplified to dB calculation:
        double rangeM = CoordinateSystem.NmToMeters(ctx.RangeNm);
        double rcsM2 = ctx.TargetRcsM2;

        // Convert to dB
        double ptDb = radar.TransmitPowerDbW;
        double gtDb = radar.AntennaGainDbi;
        double rcsDb = 10 * Math.Log10(Math.Max(rcsM2, 0.001));
        double rangeDb = 40 * Math.Log10(rangeM); // R^4

        // Noise power in dBW
        double noiseDb = BoltzmannDbW + 10 * Math.Log10(SystemNoiseTemp) +
                         10 * Math.Log10(BandwidthHz) + 3.0; // 3dB noise figure

        // Wavelength for S-band (~3GHz): λ ≈ 0.1m
        double lambdaDb = 20 * Math.Log10(0.1);

        double snrDb = ptDb + 2 * gtDb + lambdaDb + rcsDb - rangeDb
                       - 30 * Math.Log10(4 * Pi) - noiseDb; // simplified propagation loss

        // Apply weather attenuation
        snrDb -= ctx.WeatherAttenuationDbKm * ctx.RangeNm * 1.852; // per km

        // Apply ECM jamming
        if (ctx.TargetECMActive && ctx.EcmPowerW > 0)
        {
            double jamDb = 10 * Math.Log10(ctx.EcmPowerW);
            // Jamming burns through at close range but is effective far out
            double burnThroughRangeNm = 15.0; // rough burn-through
            double jammingEffect = Math.Clamp((ctx.RangeNm - 5) / (burnThroughRangeNm - 5), 0, 1);
            if (radar.HasECCM)
                jammingEffect *= 0.5; // ECCM halves jammer effectiveness
            snrDb -= jamDb * jammingEffect * 0.3;
        }

        // Apply health degradation
        snrDb += 20 * Math.Log10(radar.HealthPct);

        // ── SNR to Pd (Swerling 1 model) ─────────────────────────────
        double detectionThresholdDb = 13.0; // min SNR for ~50% Pd
        double snrLinear = Math.Pow(10, (snrDb - detectionThresholdDb) / 10.0);
        double pd = 1.0 - Math.Exp(-snrLinear);

        return Math.Clamp(pd, 0.0, 0.98);
    }

    /// <summary>Weather attenuation in dB/km based on precipitation</summary>
    public static double GetWeatherAttenuation(double precipitationMmHr)
    {
        if (precipitationMmHr <= 0) return 0.001; // Clear air
        if (precipitationMmHr < 4) return 0.01;   // Light rain
        if (precipitationMmHr < 16) return 0.05;  // Moderate rain
        return 0.2;                                // Heavy rain
    }
}
