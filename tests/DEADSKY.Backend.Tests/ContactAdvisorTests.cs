using DEADSKY.Core.Radar;

namespace DEADSKY.Backend.Tests;

public class ContactAdvisorTests
{
    [Fact]
    public void Build_ReturnsVampire_ForMissileTrack()
    {
        var track = new TrackFile
        {
            TrackId = "TRK-9001",
            TrackDesignation = "Cruise Missile",
            Classification = TrackClassification.Hostile,
            Quality = TrackQuality.Firm,
            TimeToThreatSec = 42,
            ClosingSpeedMps = 250
        };

        var advisory = ContactAdvisor.Build(track);

        Assert.Equal("VAMPIRE", advisory.Callout);
        Assert.Contains("VAMPIRE", advisory.TagsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TIME TO THREAT", advisory.TimeToThreatText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_ReturnsUnknownLowObs_ForUncertainUnknownTrack()
    {
        var track = new TrackFile
        {
            TrackId = "TRK-1002",
            TrackDesignation = "UNKNOWN",
            Classification = TrackClassification.Unknown,
            ClassificationConfidence = 0.1,
            PositionUncertaintyM = 2200,
            Quality = TrackQuality.Firm
        };

        var advisory = ContactAdvisor.Build(track);

        Assert.Contains("UNKNOWN", advisory.IdentityLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOW OBS", advisory.IdentityLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_ReturnsSamInbound_ForEngagedHotTrack()
    {
        var track = new TrackFile
        {
            TrackId = "TRK-1111",
            TrackDesignation = "MiG-29",
            Classification = TrackClassification.Hostile,
            Quality = TrackQuality.Firm,
            IsBeingEngaged = true,
            IsDesignated = true,
            TimeToThreatSec = 18,
            ClosingSpeedMps = 300
        };

        var advisory = ContactAdvisor.Build(track);

        Assert.Equal("SAM INBOUND", advisory.StateLabel);
        Assert.Contains("LOCK", advisory.TagsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ENGAGED", advisory.TagsText, StringComparison.OrdinalIgnoreCase);
    }
}
