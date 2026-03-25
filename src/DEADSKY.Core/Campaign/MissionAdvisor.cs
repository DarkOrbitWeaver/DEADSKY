using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;
using DEADSKY.Core.Weapons;

namespace DEADSKY.Core.Campaign;

public sealed record MissionAssessment(
    string MissionPhase,
    string ObjectiveStatus,
    string NextWaveStatus,
    string Recommendation,
    string SectorPressure,
    string SingleShotPkText,
    string SalvoPkText);

public static class MissionAdvisor
{
    public static MissionAssessment Build(
        SimulationSnapshot snapshot,
        ScenarioDefinition? scenario,
        int enemyBreakthroughs,
        TrackFile? selectedTrack,
        WeaponsSystem weapons)
    {
        var battery = snapshot.Battery;
        var hostiles = snapshot.HostileTracks.Count;
        var missiles = snapshot.ActiveMissiles.Count;

        string phase = missiles > 0
            ? "ENGAGEMENT IN PROGRESS"
            : hostiles > 0
                ? "TRACKING HOSTILE PACKAGE"
                : snapshot.GameTimeSec < 5
                    ? "MISSION STANDBY"
                    : "AIRSPACE STABLE";

        string objective = scenario?.VictoryConditions.Win ?? "Protect the battery and deny the raid.";
        if (enemyBreakthroughs > 0 && scenario != null)
            objective = $"BREAKTHROUGHS {enemyBreakthroughs}/{scenario.VictoryConditions.MaxEnemyBreakthroughs} | {scenario.VictoryConditions.Lose}";

        string nextWave = DescribeNextWave(scenario, snapshot.GameTimeSec);
        string pressure = hostiles switch
        {
            >= 5 => "SECTOR PRESSURE: CRITICAL",
            >= 3 => "SECTOR PRESSURE: HIGH",
            >= 1 => "SECTOR PRESSURE: ELEVATED",
            _ => "SECTOR PRESSURE: LOW"
        };

        string recommendation = BuildRecommendation(snapshot, selectedTrack);
        string pkText = "PK: STANDBY";
        string salvoPkText = "SALVO PK: STANDBY";

        if (battery != null && selectedTrack != null)
        {
            double pk = weapons.CalculatePk(battery, selectedTrack);
            pkText = $"PK: {pk:P0}";

            int salvoCount = Math.Max(1, Math.Min(2, battery.ReadyLaunchers));
            double salvoPk = weapons.CalculateSalvoPk(battery, selectedTrack, salvoCount);
            salvoPkText = $"SALVO PK ({salvoCount}): {salvoPk:P0}";
        }

        return new MissionAssessment(
            MissionPhase: phase,
            ObjectiveStatus: objective,
            NextWaveStatus: nextWave,
            Recommendation: recommendation,
            SectorPressure: pressure,
            SingleShotPkText: pkText,
            SalvoPkText: salvoPkText);
    }

    public static string DescribeNextWave(ScenarioDefinition? scenario, double gameTimeSec)
    {
        if (scenario == null || scenario.EnemyForces.Waves.Count == 0)
            return "NEXT WAVE: NONE";

        var nextWave = scenario.EnemyForces.Waves
            .Where(w => w.Trigger.Equals("time", StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.TimeMinutes)
            .FirstOrDefault(w => w.TimeMinutes * 60 > gameTimeSec);

        if (nextWave == null)
            return "NEXT WAVE: ALL COMMITTED";

        double etaSec = Math.Max(0, nextWave.TimeMinutes * 60 - gameTimeSec);
        int aircraftCount = nextWave.Aircraft.Sum(a => Math.Max(1, a.Count));
        return $"NEXT WAVE: {aircraftCount} AIRFRAME ETA {etaSec / 60:0.0} MIN";
    }

    private static string BuildRecommendation(SimulationSnapshot snapshot, TrackFile? selectedTrack)
    {
        var battery = snapshot.Battery;
        if (battery == null)
            return "RECOMMENDATION: SYSTEM CHECK";

        if (snapshot.RadarMode == Entities.RadarMode.Silent && snapshot.HostileTracks.Count > 0)
            return "RECOMMENDATION: REACTIVATE SEARCH OR TWS";

        if (selectedTrack == null)
            return snapshot.HostileTracks.Count > 0
                ? "RECOMMENDATION: SORT HIGHEST THREAT TRACK"
                : "RECOMMENDATION: MAINTAIN SEARCH PATTERN";

        if (selectedTrack.RangeNm > battery.MissileMaxRangeNm)
            return "RECOMMENDATION: HOLD FIRE, CONTINUE TRACK";

        if (battery.ROE == Entities.RulesOfEngagement.WeaponsHold)
            return "RECOMMENDATION: REQUEST RELEASE OR HOLD DEFENSIVE";

        if (battery.ROE == Entities.RulesOfEngagement.WeaponsTight &&
            selectedTrack.Classification is not (TrackClassification.Hostile or TrackClassification.AssumedHostile))
        {
            return "RECOMMENDATION: REQUEST DECLARE";
        }

        if (battery.ReadyLaunchers == 0)
            return "RECOMMENDATION: RELOAD CYCLE ACTIVE, DELAY ENGAGEMENT";

        if (snapshot.ActiveMissiles.Count > 0)
            return "RECOMMENDATION: MAINTAIN ILLUMINATION AND MONITOR SPLASH";

        return selectedTrack.ThreatLevel >= 0.7
            ? "RECOMMENDATION: DESIGNATE AND ENGAGE"
            : "RECOMMENDATION: TRACK, CLASSIFY, AND TIME SHOT";
    }
}
