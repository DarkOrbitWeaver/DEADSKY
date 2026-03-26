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

}
