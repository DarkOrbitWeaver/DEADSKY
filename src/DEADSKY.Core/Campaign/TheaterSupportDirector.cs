using DEADSKY.Core.Entities;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Campaign;

public sealed record TheaterSupportAdjustment(
    string Summary,
    int MissileReservePenalty,
    double RadarRangeMultiplier,
    double PkModifier,
    bool CommandNetStrained);

public static class TheaterSupportDirector
{
    public static TheaterSupportAdjustment Apply(SectorCampaignState? sectorState, ScenarioDefinition scenario, SimulationEngine sim)
    {
        var battery = sim.Entities.GetPlayerBattery();
        if (battery == null || sectorState == null || sectorState.Objectives.Count == 0)
        {
            return new TheaterSupportAdjustment(
                "THEATER SUPPORT: FULL STOCKS AND NETWORK COVERAGE AVAILABLE.",
                0,
                1.0,
                0.0,
                false);
        }

        int missilePenalty = 0;
        double radarMultiplier = 1.0;
        double pkModifier = 0.0;
        bool commandNetStrained = false;
        var notes = new List<string>();

        foreach (var objective in sectorState.Objectives)
        {
            double damage = 1.0 - Math.Clamp(objective.IntegrityPct, 0, 1);
            if (damage <= 0.05)
                continue;

            string text = $"{objective.ObjectiveId} {objective.ObjectiveName} {objective.Importance}".ToLowerInvariant();

            if (text.Contains("depot") || text.Contains("fuel") || text.Contains("ammo") || text.Contains("airfield"))
            {
                int penalty = damage switch
                {
                    >= 0.35 => 4,
                    >= 0.18 => 2,
                    _ => 1
                };
                missilePenalty += penalty;
                notes.Add($"{objective.ObjectiveName.ToUpperInvariant()} SUPPLY LOSS -{penalty} MISSILES");
            }

            if (text.Contains("relay") || text.Contains("command") || text.Contains("c2"))
            {
                double multiplierDrop = damage switch
                {
                    >= 0.35 => 0.18,
                    >= 0.18 => 0.10,
                    _ => 0.05
                };
                radarMultiplier *= 1.0 - multiplierDrop;
                commandNetStrained = true;
                notes.Add($"{objective.ObjectiveName.ToUpperInvariant()} DEGRADED NETWORK CUES");
            }

            if (objective.LastCondition is ObjectiveCondition.Breached or ObjectiveCondition.UnderAttack)
            {
                pkModifier -= objective.Importance.Equals("primary", StringComparison.OrdinalIgnoreCase) ? 0.02 : 0.01;
            }
        }

        if (missilePenalty > 0)
            battery.ReserveMissiles = Math.Max(0, battery.ReserveMissiles - missilePenalty);

        if (radarMultiplier < 1.0)
        {
            battery.RadarRangeNm *= Math.Max(0.75, radarMultiplier);
            sim.Radar.SetRange(battery.RadarRangeNm);
        }

        if (pkModifier != 0)
            battery.MissileSingleShotPk = Math.Clamp(battery.MissileSingleShotPk + pkModifier, 0.45, 0.95);

        string summary = notes.Count == 0
            ? "THEATER SUPPORT: FULL STOCKS AND NETWORK COVERAGE AVAILABLE."
            : $"THEATER SUPPORT: {string.Join(" | ", notes)}";

        return new TheaterSupportAdjustment(
            summary,
            missilePenalty,
            radarMultiplier,
            pkModifier,
            commandNetStrained);
    }
}
