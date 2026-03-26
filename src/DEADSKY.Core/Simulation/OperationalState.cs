using DEADSKY.Core.Campaign;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Weapons;

namespace DEADSKY.Core.Simulation;

public enum LockCueState
{
    Search,
    TrackHold,
    HardLock,
    MissileInbound
}

public enum IncidentSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record TrackThreatState(
    string TrackId,
    LockCueState LockCue,
    bool RadarSupportAvailable,
    bool MissileInbound,
    bool CountermeasureActive,
    double Confidence,
    string Summary);

public sealed record FriendlyForceState(
    string Id,
    string Callsign,
    string Role,
    string Status,
    bool VisibleInPicture,
    string Summary);

public sealed record EngagementIncident(
    string IncidentType,
    string Summary,
    IncidentSeverity Severity,
    DateTime TimestampUtc,
    string? TrackId = null,
    string? EntityId = null,
    string? WeaponId = null);

public sealed record SharedOperationalPicture(
    string ThreatSummary,
    string SupportSummary,
    string ConsequenceSummary,
    IReadOnlyList<TrackThreatState> ThreatStates,
    IReadOnlyList<FriendlyForceState> FriendlyForces);

public static class OperationalPictureBuilder
{
    public static IReadOnlyList<TrackThreatState> BuildThreatStates(SimulationSnapshot snapshot)
    {
        var states = new List<TrackThreatState>();
        foreach (var track in snapshot.AllTracks)
        {
            bool hardLock = track.IsDesignated ||
                            (!string.IsNullOrWhiteSpace(snapshot.Battery?.DesignatedTargetId) &&
                             snapshot.Battery.DesignatedTargetId == track.EntityId);
            bool trackHold = track.IsTrackHeld && !hardLock;
            bool missileInbound = track.IsBeingEngaged;
            bool countermeasureActive = false;

            var aircraft = track.EntityId == null
                ? null
                : snapshot.HostileAircraft.FirstOrDefault(entity => entity.Id == track.EntityId);
            if (aircraft != null)
                countermeasureActive = aircraft.ECMActive || aircraft.IsChaffActive || aircraft.IsFlareActive;

            LockCueState cue = missileInbound
                ? LockCueState.MissileInbound
                : hardLock
                    ? LockCueState.HardLock
                    : trackHold
                        ? LockCueState.TrackHold
                        : LockCueState.Search;

            bool radarSupport = snapshot.Battery?.RadarOnline == true &&
                                snapshot.Battery.RadarMode is RadarMode.TrackWhileScan or RadarMode.SingleTargetTrack;

            string summary = cue switch
            {
                LockCueState.MissileInbound => "Missile support active.",
                LockCueState.HardLock => "Hard-lock warning.",
                LockCueState.TrackHold => "Tracked and sorted.",
                _ => "Search picture only."
            };

            if (countermeasureActive)
                summary += " Countermeasures detected.";

            states.Add(new TrackThreatState(
                track.TrackId,
                cue,
                radarSupport,
                missileInbound,
                countermeasureActive,
                Math.Clamp(track.ClassificationConfidence, 0.0, 1.0),
                summary));
        }

        return states;
    }

    public static IReadOnlyList<FriendlyForceState> BuildFriendlyForceStates(IEnumerable<FriendlySupportPackage>? supportPackages)
    {
        if (supportPackages == null)
            return Array.Empty<FriendlyForceState>();

        return supportPackages
            .Select(package => new FriendlyForceState(
                package.Id,
                package.UnitCallsign,
                package.Type.ToString(),
                package.Availability.ToString(),
                package.IsVisibleInPicture,
                package.LastSummary))
            .ToList();
    }

    public static SharedOperationalPicture Build(
        SimulationSnapshot snapshot,
        IEnumerable<EngagementIncident>? incidents = null,
        IEnumerable<FriendlySupportPackage>? supportPackages = null)
    {
        var threatStates = BuildThreatStates(snapshot);
        var friendlyForces = BuildFriendlyForceStates(supportPackages);
        var recentIncident = incidents?
            .OrderByDescending(incident => incident.TimestampUtc)
            .FirstOrDefault();

        string threatSummary = snapshot.HostileTracks.Count == 0
            ? "Air picture clean."
            : $"{snapshot.HostileTracks.Count} hostile tracks, {snapshot.ActiveMissiles.Count} friendly missiles in flight.";
        string supportSummary = friendlyForces.Count == 0
            ? "No visible support actors."
            : string.Join(" | ", friendlyForces
                .Where(force => force.VisibleInPicture)
                .Select(force => $"{force.Callsign} {force.Status}")
                .DefaultIfEmpty("Support actors currently off picture."));
        string consequenceSummary = recentIncident == null
            ? "No recent incidents."
            : $"{recentIncident.IncidentType.ToUpperInvariant()}: {recentIncident.Summary}";

        return new SharedOperationalPicture(
            threatSummary,
            supportSummary,
            consequenceSummary,
            threatStates,
            friendlyForces);
    }
}
