namespace DEADSKY.Core.Radar;

public sealed record ContactAdvisory(
    string Callout,
    string IdentityLabel,
    string StateLabel,
    string TimeToThreatText,
    string TagsText);

public static class ContactAdvisor
{
    public static ContactAdvisory Build(TrackFile track)
    {
        bool isMissile = IsMissileTrack(track);
        bool suspectJammer = IsJammerTrack(track);
        bool possibleDecoy = IsDecoyTrack(track);
        bool lowAltMasked = track.AltitudeFt < 1200 && !track.IFFResponse;
        string callout = isMissile
            ? "VAMPIRE"
            : suspectJammer
                ? "MUSIC"
            : track.Classification switch
            {
                TrackClassification.Hostile => "BANDIT",
                TrackClassification.AssumedHostile => "SUSPECT",
                TrackClassification.Friendly => "FRIENDLY",
                TrackClassification.Neutral => "NEUTRAL",
                TrackClassification.Civilian => "CIVILIAN",
                _ => "BOGEY"
            };

        string identity = track.Classification switch
        {
            TrackClassification.Hostile => "CONFIRMED HOSTILE",
            TrackClassification.AssumedHostile => "ASSUMED HOSTILE",
            TrackClassification.Friendly => "FRIENDLY IFF",
            TrackClassification.Neutral => "NEUTRAL",
            TrackClassification.Civilian => "CIVIL TRAFFIC",
            _ when track.PositionUncertaintyM > 1500 || track.ClassificationConfidence < 0.35 => "UNKNOWN / POSSIBLE LOW OBS",
            _ when suspectJammer => "SUSPECT JAMMER / OFFSET ESCORT",
            _ when possibleDecoy => "UNKNOWN / POSSIBLE DECOY",
            _ when lowAltMasked => "UNKNOWN / LOW-ALT MASKED",
            _ => "UNKNOWN"
        };

        string state = track.Quality switch
        {
            TrackQuality.Lost => "TRACK LOST",
            TrackQuality.Fading => "FADE / COAST",
            _ when track.IsBeingEngaged => "SAM INBOUND",
            _ when isMissile => "MISSILE INBOUND",
            _ when suspectJammer => "JAMMING / OFFSET",
            _ when possibleDecoy => "PROBABLE FEINT",
            _ when lowAltMasked => "MASKED / TERRAIN FOLLOW",
            _ when track.IsHot => "INBOUND",
            _ => "MANEUVER / COLD"
        };

        string timeToThreat = double.IsFinite(track.TimeToThreatSec) && track.TimeToThreatSec < double.MaxValue / 2
            ? $"TIME TO THREAT: {Math.Max(0, track.TimeToThreatSec):0}s"
            : "TIME TO THREAT: ---";

        var tags = new List<string>();

        if (track.IsDesignated)
            tags.Add("LOCK");

        if (track.IsBeingEngaged)
            tags.Add("ENGAGED");

        if (!track.IFFResponse && track.IFFInterrogated)
            tags.Add("NO-IFF");

        if (track.IsHot)
            tags.Add("HOT");
        else
            tags.Add("COLD");

        if (track.Quality == TrackQuality.Fading)
            tags.Add("FADE");

        if (track.PositionUncertaintyM > 1500)
            tags.Add("UNCERTAIN");

        if (suspectJammer)
            tags.Add("JAMMER");

        if (possibleDecoy)
            tags.Add("DECOY?");

        if (lowAltMasked)
            tags.Add("LOW-ALT");

        if (isMissile)
            tags.Add("VAMPIRE");

        return new ContactAdvisory(
            Callout: callout,
            IdentityLabel: identity,
            StateLabel: state,
            TimeToThreatText: timeToThreat,
            TagsText: tags.Count == 0 ? "NONE" : string.Join(" | ", tags));
    }

    private static bool IsMissileTrack(TrackFile track)
    {
        string designation = track.TrackDesignation.ToUpperInvariant();
        return designation.Contains("MISSILE", StringComparison.Ordinal) ||
               designation.Contains("CRUISE", StringComparison.Ordinal) ||
               designation.Contains("KH-", StringComparison.Ordinal) ||
               designation.Contains("AGM-", StringComparison.Ordinal) ||
               designation.Contains("9M", StringComparison.Ordinal);
    }

    private static bool IsJammerTrack(TrackFile track)
    {
        string designation = track.TrackDesignation.ToUpperInvariant();
        string group = track.GroupLabel.ToUpperInvariant();
        return designation.Contains("EA-6", StringComparison.Ordinal) ||
               designation.Contains("PROWLER", StringComparison.Ordinal) ||
               designation.Contains("ECM", StringComparison.Ordinal) ||
               group.Contains("SPECTER", StringComparison.Ordinal);
    }

    private static bool IsDecoyTrack(TrackFile track)
    {
        string designation = track.TrackDesignation.ToUpperInvariant();
        string group = track.GroupLabel.ToUpperInvariant();
        return designation.Contains("DRONE", StringComparison.Ordinal) ||
               designation.Contains("MQ-9", StringComparison.Ordinal) ||
               group.Contains("SHADE", StringComparison.Ordinal);
    }
}
