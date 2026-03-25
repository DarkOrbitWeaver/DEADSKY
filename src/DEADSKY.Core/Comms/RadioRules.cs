namespace DEADSKY.Core.Comms;

public enum RadioChannelKind
{
    Command,
    Battery,
    AirDefense,
    Intel,
    Guard,
    Open
}

public enum MessageTone
{
    Formal,
    Tactical,
    Advisory,
    Crew,
    Emergency,
    Hostile,
    Intercepted,
    OpenBroadcast,
    System
}

public enum SpeakerAffiliation
{
    Player,
    Allied,
    FriendlySupport,
    Enemy,
    Civilian,
    System
}

public sealed record RadioSpeakerProfile
{
    public string SenderCallsign { get; init; } = "";
    public string SpeakerDisplayName { get; init; } = "";
    public string RankOrRole { get; init; } = "";
    public string UnitCallsign { get; init; } = "";
    public string Designation { get; init; } = "";
    public SpeakerAffiliation Affiliation { get; init; } = SpeakerAffiliation.Allied;
    public bool ProfessionalDiscipline { get; init; } = true;
    public bool AllowsOpenFrequencyProfanity { get; init; }

    public string HeaderIdentity =>
        !string.IsNullOrWhiteSpace(RankOrRole) && !string.IsNullOrWhiteSpace(UnitCallsign)
            ? $"{RankOrRole} {SpeakerDisplayName} // {UnitCallsign}"
            : !string.IsNullOrWhiteSpace(UnitCallsign)
                ? UnitCallsign
                : !string.IsNullOrWhiteSpace(SpeakerDisplayName)
                    ? SpeakerDisplayName
                    : SenderCallsign;
}

public sealed record OpenFrequencyEvent
{
    public string Intent { get; init; } = "warning";
    public string Summary { get; init; } = "";
    public string SuggestedReply { get; init; } = "";
    public bool RequiresAttention { get; init; }
}

public sealed record RadioTransmissionDirective(
    RadioChannel EffectiveChannel,
    MessageType EffectiveType,
    MessageTone Tone,
    string SanitizedContent,
    bool RequiresAttention,
    bool CanReply,
    string PriorityTag,
    string ChannelTag);

public static class RadioRules
{
    private static readonly Dictionary<string, string> SanitizedTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fuck"] = "[redacted]",
        ["fucking"] = "[redacted]",
        ["shit"] = "[redacted]",
        ["bastard"] = "[redacted]",
        ["asshole"] = "[redacted]",
        ["damn"] = "damn"
    };

    public static RadioChannelKind GetChannelKind(RadioChannel channel) => channel switch
    {
        RadioChannel.CommandNet => RadioChannelKind.Command,
        RadioChannel.BatteryNet => RadioChannelKind.Battery,
        RadioChannel.AirDefenseNet => RadioChannelKind.AirDefense,
        RadioChannel.IntelNet => RadioChannelKind.Intel,
        RadioChannel.Guard => RadioChannelKind.Guard,
        _ => RadioChannelKind.Open
    };

    public static RadioSpeakerProfile CreatePlayerProfile() => new()
    {
        SenderCallsign = "ALPHA ACTUAL",
        SpeakerDisplayName = "ALPHA ACTUAL",
        RankOrRole = "LT",
        UnitCallsign = "ALPHA",
        Designation = "BATTERY ACTUAL",
        Affiliation = SpeakerAffiliation.Player
    };

    public static RadioSpeakerProfile CreateAlliedHQProfile(string callsign = "ECHO ACTUAL") => new()
    {
        SenderCallsign = callsign,
        SpeakerDisplayName = callsign,
        RankOrRole = "SECTOR HQ",
        UnitCallsign = "ECHO",
        Designation = "AIR DEFENSE COMMAND",
        Affiliation = SpeakerAffiliation.Allied
    };

    public static RadioSpeakerProfile CreateIntelProfile(string callsign = "INTEL-1") => new()
    {
        SenderCallsign = callsign,
        SpeakerDisplayName = callsign,
        RankOrRole = "INTEL",
        UnitCallsign = "SIGMA CELL",
        Designation = "THREAT ANALYSIS",
        Affiliation = SpeakerAffiliation.Allied
    };

    public static RadioSpeakerProfile CreateCrewProfile(string senderName, string designation = "ALPHA FIRE UNIT") => new()
    {
        SenderCallsign = senderName.ToUpperInvariant(),
        SpeakerDisplayName = senderName.ToUpperInvariant(),
        RankOrRole = "SGT",
        UnitCallsign = designation.ToUpperInvariant(),
        Designation = "BATTERY CREW",
        Affiliation = SpeakerAffiliation.Allied
    };

    public static RadioSpeakerProfile CreateFriendlySupportProfile(
        string displayName,
        string rankOrRole,
        string unitCallsign,
        string designation) => new()
    {
        SenderCallsign = displayName.ToUpperInvariant(),
        SpeakerDisplayName = displayName.ToUpperInvariant(),
        RankOrRole = rankOrRole.ToUpperInvariant(),
        UnitCallsign = unitCallsign.ToUpperInvariant(),
        Designation = designation.ToUpperInvariant(),
        Affiliation = SpeakerAffiliation.FriendlySupport
    };

    public static RadioSpeakerProfile CreateEnemyProfile(
        string displayName,
        string unitCallsign,
        string designation,
        bool allowProfanity = true) => new()
    {
        SenderCallsign = displayName.ToUpperInvariant(),
        SpeakerDisplayName = displayName.ToUpperInvariant(),
        RankOrRole = "HOSTILE",
        UnitCallsign = unitCallsign.ToUpperInvariant(),
        Designation = designation.ToUpperInvariant(),
        Affiliation = SpeakerAffiliation.Enemy,
        ProfessionalDiscipline = false,
        AllowsOpenFrequencyProfanity = allowProfanity
    };

    public static RadioSpeakerProfile CreateSystemProfile(string displayName = "ALERT") => new()
    {
        SenderCallsign = displayName.ToUpperInvariant(),
        SpeakerDisplayName = displayName.ToUpperInvariant(),
        RankOrRole = "SYSTEM",
        UnitCallsign = "NET CTRL",
        Designation = "AUTOMATION",
        Affiliation = SpeakerAffiliation.System,
        ProfessionalDiscipline = true
    };

    public static RadioTransmissionDirective PrepareTransmission(
        RadioSpeakerProfile speaker,
        RadioChannel requestedChannel,
        string content,
        MessagePriority priority,
        MessageType type,
        bool canReply = false)
    {
        var effectiveChannel = ResolveChannel(requestedChannel, speaker, type);
        var effectiveType = ResolveType(type, speaker, effectiveChannel);
        var tone = ResolveTone(speaker, effectiveChannel, effectiveType, priority);
        var sanitized = SanitizeContent(content, speaker, effectiveChannel);
        bool requiresAttention = priority >= MessagePriority.Priority ||
                                 effectiveType is MessageType.Alert or MessageType.Intercept;
        bool actionable = canReply ||
                          (!speaker.Affiliation.Equals(SpeakerAffiliation.Player) &&
                           effectiveType is not MessageType.Alert &&
                           effectiveChannel is not RadioChannel.AirDefenseNet);

        return new RadioTransmissionDirective(
            effectiveChannel,
            effectiveType,
            tone,
            sanitized,
            requiresAttention,
            actionable,
            PriorityToTag(priority),
            ChannelToTag(effectiveChannel));
    }

    public static string BuildGuidanceSummary() =>
        "RULES: COMMAND/BATTERY/INTEL stay disciplined. AIR DEFENSE handles cross-battery control. GUARD is emergency-only. OPEN may carry warnings, panic, surrender calls, or hostile chatter.";

    private static RadioChannel ResolveChannel(RadioChannel requestedChannel, RadioSpeakerProfile speaker, MessageType type)
    {
        if (speaker.Affiliation == SpeakerAffiliation.Enemy &&
            requestedChannel is RadioChannel.CommandNet or RadioChannel.BatteryNet or RadioChannel.AirDefenseNet)
        {
            return RadioChannel.IntelNet;
        }

        if (requestedChannel == RadioChannel.Guard && type is not MessageType.Alert && speaker.Affiliation == SpeakerAffiliation.Enemy)
        {
            return RadioChannel.OpenFreq;
        }

        return requestedChannel;
    }

    private static MessageType ResolveType(MessageType requestedType, RadioSpeakerProfile speaker, RadioChannel effectiveChannel)
    {
        if (speaker.Affiliation == SpeakerAffiliation.Enemy &&
            effectiveChannel == RadioChannel.IntelNet &&
            requestedType != MessageType.Alert)
        {
            return MessageType.Intercept;
        }

        return requestedType;
    }

    private static MessageTone ResolveTone(
        RadioSpeakerProfile speaker,
        RadioChannel channel,
        MessageType type,
        MessagePriority priority)
    {
        if (type == MessageType.Alert || priority == MessagePriority.Flash)
            return MessageTone.Emergency;

        if (type == MessageType.Intercept)
            return MessageTone.Intercepted;

        if (channel == RadioChannel.BatteryNet)
            return MessageTone.Crew;

        if (channel == RadioChannel.OpenFreq)
            return speaker.Affiliation == SpeakerAffiliation.Enemy
                ? MessageTone.Hostile
                : MessageTone.OpenBroadcast;

        if (channel == RadioChannel.IntelNet)
            return MessageTone.Advisory;

        if (speaker.Affiliation == SpeakerAffiliation.System)
            return MessageTone.System;

        return MessageTone.Tactical;
    }

    private static string SanitizeContent(string content, RadioSpeakerProfile speaker, RadioChannel channel)
    {
        if (channel == RadioChannel.OpenFreq && speaker.AllowsOpenFrequencyProfanity)
            return content.Trim();

        string sanitized = content;
        foreach (var pair in SanitizedTerms)
            sanitized = sanitized.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);

        return sanitized.Trim();
    }

    private static string PriorityToTag(MessagePriority priority) => priority switch
    {
        MessagePriority.Flash => "FLASH",
        MessagePriority.Immediate => "IMMEDIATE",
        MessagePriority.Priority => "PRIORITY",
        _ => "ROUTINE"
    };

    private static string ChannelToTag(RadioChannel channel) => channel switch
    {
        RadioChannel.CommandNet => "CMD",
        RadioChannel.BatteryNet => "BAT",
        RadioChannel.AirDefenseNet => "ADF",
        RadioChannel.IntelNet => "INT",
        RadioChannel.Guard => "GDR",
        _ => "OPEN"
    };
}
