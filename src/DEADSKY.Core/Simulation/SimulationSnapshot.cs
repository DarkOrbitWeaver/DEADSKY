using DEADSKY.Core.Entities;
using DEADSKY.Core.Personnel;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Weapons;

namespace DEADSKY.Core.Simulation;

public sealed class WeatherState
{
    public double VisibilityNm { get; set; } = 80;
    public double CloudCeilingFt { get; set; } = 25000;
    public double PrecipitationMmHr { get; set; }
    public string Description { get; set; } = "Clear";
    public double WindSpeedKts { get; set; } = 8;
    public double WindDirectionDeg { get; set; } = 180;

    public WeatherState Clone() => new()
    {
        VisibilityNm = VisibilityNm,
        CloudCeilingFt = CloudCeilingFt,
        PrecipitationMmHr = PrecipitationMmHr,
        Description = Description,
        WindSpeedKts = WindSpeedKts,
        WindDirectionDeg = WindDirectionDeg
    };
}

public sealed class SimulationSnapshot
{
    public double GameTimeSec { get; init; }
    public string GameTimeString { get; init; } = "00:00:00 ZULU";
    public SAMBattery? Battery { get; init; }
    public CrewRoster? Crew { get; init; }
    public IReadOnlyList<TrackFile> AllTracks { get; init; } = Array.Empty<TrackFile>();
    public IReadOnlyList<TrackFile> FirmTracks { get; init; } = Array.Empty<TrackFile>();
    public IReadOnlyList<TrackFile> HostileTracks { get; init; } = Array.Empty<TrackFile>();
    public IReadOnlyList<Aircraft> HostileAircraft { get; init; } = Array.Empty<Aircraft>();
    public IReadOnlyList<SAMMissile> ActiveMissiles { get; init; } = Array.Empty<SAMMissile>();
    public IReadOnlyList<RadarSystem.EcmEffect> ActiveEcmEffects { get; init; } = Array.Empty<RadarSystem.EcmEffect>();
    public IReadOnlyList<WeaponDefinition> AvailableWeapons { get; init; } = Array.Empty<WeaponDefinition>();
    public WeaponDefinition? SelectedWeapon { get; init; }
    public IReadOnlyList<TrackThreatState> TrackThreatStates { get; init; } = Array.Empty<TrackThreatState>();
    public IReadOnlyList<EngagementIncident> RecentIncidents { get; init; } = Array.Empty<EngagementIncident>();
    public double RadarSweepAngle { get; init; }
    public double RadarRangeNm { get; init; }
    public RadarMode RadarMode { get; init; } = RadarMode.Search;
    public WeatherState Weather { get; init; } = new();
}
