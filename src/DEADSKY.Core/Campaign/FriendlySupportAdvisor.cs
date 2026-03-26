using DEADSKY.Core.Comms;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Campaign;

public static class FriendlySupportAdvisor
{
    public static string? BuildTacticalUpdate(FriendlySupportPackage package, SimulationSnapshot snapshot)
    {
        var hostiles = snapshot.HostileTracks
            .OrderByDescending(track => track.ThreatLevel)
            .ToList();
        var primary = hostiles.FirstOrDefault();
        string axis = primary == null ? "SECTOR" : $"{DescribeAxis(primary.BearingDeg)} AXIS";

        return package.Type switch
        {
            FriendlySupportType.CombatAirPatrol => hostiles.Count == 0
                ? $"{package.UnitCallsign}, CAP holding west screen. No hostile commit in sector."
                : hostiles.Any(track => track.RangeNm <= 45)
                    ? $"{package.UnitCallsign}, CAP on station. Leakers pressing {axis}. Ready to commit."
                    : $"{package.UnitCallsign}, CAP screening {axis}. Hostile raid still outside commit line.",

            FriendlySupportType.JammingSupport => hostiles.Count == 0
                ? $"{package.UnitCallsign}, standoff jam orbit set. Holding emissions in reserve."
                : $"{package.UnitCallsign}, standoff jam active. Expect degraded raid coordination on {axis}.",

            FriendlySupportType.NearbyBattery => hostiles.Count == 0
                ? $"{package.UnitCallsign}, crossfire lane held. No shot call yet."
                : hostiles.Any(track => track.RangeNm <= 50)
                    ? $"{package.UnitCallsign}, crossfire lane hot on {axis}. Ready to support your shot doctrine."
                    : $"{package.UnitCallsign}, tracking {hostiles.Count} hostiles. Crossfire lane established on {axis}.",

            FriendlySupportType.Awacs => hostiles.Count == 0
                ? $"{package.UnitCallsign}, wide-area picture clean. No raid commit outside sector."
                : $"{package.UnitCallsign}, picture fused. {hostiles.Count} hostile tracks sorted. Primary axis {axis}.",

            _ => null
        };
    }

    public static RadioChannel ResolveReportChannel(FriendlySupportType type) => type switch
    {
        FriendlySupportType.NearbyBattery => RadioChannel.AirDefenseNet,
        FriendlySupportType.Awacs => RadioChannel.IntelNet,
        _ => RadioChannel.CommandNet
    };

    private static string DescribeAxis(double bearingDeg)
    {
        double normalized = (bearingDeg % 360 + 360) % 360;
        return normalized switch
        {
            >= 315 or < 45 => "NORTH",
            >= 45 and < 135 => "EAST",
            >= 135 and < 225 => "SOUTH",
            _ => "WEST"
        };
    }
}
