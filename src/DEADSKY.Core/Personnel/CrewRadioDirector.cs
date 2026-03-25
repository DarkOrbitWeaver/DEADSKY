using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Personnel;

public static class CrewRadioDirector
{
    public static Soldier SelectResponder(CrewRoster roster, string playerMessage)
    {
        string normalized = playerMessage.ToLowerInvariant();

        SoldierRole preferredRole = normalized switch
        {
            var m when m.Contains("picture") || m.Contains("contact") || m.Contains("track") || m.Contains("radar") => SoldierRole.RadarOperator,
            var m when m.Contains("fire") || m.Contains("engage") || m.Contains("salvo") || m.Contains("designate") => SoldierRole.FireControl,
            var m when m.Contains("reload") || m.Contains("launcher") || m.Contains("missile") => SoldierRole.LauncherChief,
            var m when m.Contains("echo") || m.Contains("intel") || m.Contains("radio") || m.Contains("net") => SoldierRole.Signals,
            _ => SoldierRole.Commander
        };

        return roster.Soldiers
            .Where(s => s.Health != HealthStatus.KIA)
            .OrderByDescending(s => s.Role == preferredRole)
            .ThenByDescending(s => s.Proficiency)
            .First();
    }

    public static string GetPersonalitySummary(Soldier soldier) => soldier.Role switch
    {
        SoldierRole.Commander => "calm commander, clipped phrasing, decisive under pressure",
        SoldierRole.RadarOperator => "focused sensor operator, technical, alert to bearings and ranges",
        SoldierRole.FireControl => "precise weapons controller, procedural, concise",
        SoldierRole.LauncherChief => "practical launcher chief, blunt, reports readiness and reloads",
        SoldierRole.Signals => "signals specialist, radio discipline, relays and confirms orders",
        _ => "professional air defense crew member"
    };

    public static string BuildFallbackLine(Soldier soldier, string playerMessage, SimulationSnapshot snapshot)
    {
        string normalized = playerMessage.ToLowerInvariant();
        int hostiles = snapshot.HostileTracks.Count;
        int readyLaunchers = snapshot.Battery?.ReadyLaunchers ?? 0;

        return soldier.Role switch
        {
            SoldierRole.RadarOperator when normalized.Contains("picture") =>
                $"Picture update. {hostiles} hostile track{(hostiles == 1 ? string.Empty : "s")} on scope, highest threat still closing.",
            SoldierRole.FireControl when normalized.Contains("fire") || normalized.Contains("engage") =>
                $"Fire control ready. {readyLaunchers} launcher{(readyLaunchers == 1 ? string.Empty : "s")} available, awaiting commit.",
            SoldierRole.LauncherChief when normalized.Contains("reload") || normalized.Contains("missile") =>
                $"Launch section copies. Reserve count holding at {snapshot.Battery?.ReserveMissiles ?? 0}, reload cycle monitored.",
            SoldierRole.Signals when normalized.Contains("echo") || normalized.Contains("intel") =>
                "Signals copies. External net traffic clean, routing command and intel traffic now.",
            SoldierRole.Commander =>
                $"Command copies. Maintain discipline and keep the battery aligned with mission objective. Hostile count {hostiles}.",
            _ =>
                "Copy. Battery standing by for next tasking."
        };
    }
}
