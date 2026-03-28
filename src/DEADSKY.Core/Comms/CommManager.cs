using DEADSKY.Core.Entities;
using DEADSKY.Core.Logging;
using System.Collections.Concurrent;

namespace DEADSKY.Core.Comms;

public enum RadioChannel
{
    CommandNet = 1,
    BatteryNet = 2,
    AirDefenseNet = 3,
    IntelNet = 4,
    Guard = 5,
    OpenFreq = 6,
    BatteryNetwork = 7  // Phase 4: Battery-to-battery coordination
}

public enum MessagePriority
{
    Flash = 4,
    Immediate = 3,
    Priority = 2,
    Routine = 1
}

public enum MessageType
{
    Normal,
    StatusReport,
    IntelUpdate,
    Alert,
    PlayerMessage,
    RadioChatter,
    Intercept
}

public record RadioMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public RadioChannel Channel { get; init; }
    public RadioChannelKind ChannelKind { get; init; } = RadioChannelKind.Command;
    public string SenderCallsign { get; init; } = "";
    public string? RecipientCallsign { get; init; }
    public string Content { get; init; } = "";
    public MessagePriority Priority { get; init; } = MessagePriority.Routine;
    public MessageType Type { get; init; } = MessageType.Normal;
    public MessageTone MessageTone { get; init; } = MessageTone.Tactical;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsFromPlayer { get; init; }
    public bool IsRead { get; set; }
    public double StaticLevel { get; init; } = 0.15;
    public string SpeakerDisplayName { get; init; } = "";
    public string RankOrRole { get; init; } = "";
    public string UnitCallsign { get; init; } = "";
    public string Designation { get; init; } = "";
    public string PriorityTag { get; init; } = "ROUTINE";
    public bool RequiresAttention { get; init; }
    public bool CanReply { get; init; }
    public string? ConversationId { get; init; }
    public SpeakerAffiliation SpeakerAffiliation { get; init; } = SpeakerAffiliation.Allied;

    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
    public string ChannelTag => Channel switch
    {
        RadioChannel.CommandNet => "CMD",
        RadioChannel.BatteryNet => "BAT",
        RadioChannel.AirDefenseNet => "ADF",
        RadioChannel.IntelNet => "INT",
        RadioChannel.Guard => "GDR",
        _ => "OPEN"
    };

    public string DisplayHeader
    {
        get
        {
            var identity = !string.IsNullOrWhiteSpace(RankOrRole) && !string.IsNullOrWhiteSpace(UnitCallsign)
                ? $"{RankOrRole} {SpeakerDisplayName} // {UnitCallsign}"
                : !string.IsNullOrWhiteSpace(SpeakerDisplayName)
                    ? SpeakerDisplayName
                    : SenderCallsign;

            return RecipientCallsign != null
                ? $"{identity} -> {RecipientCallsign}"
                : identity;
        }
    }

    public string CompactDisplayHeader =>
        ShortenLabel(
            !string.IsNullOrWhiteSpace(UnitCallsign)
                ? UnitCallsign
                : !string.IsNullOrWhiteSpace(SenderCallsign)
                    ? SenderCallsign
                    : SpeakerDisplayName,
            14);

    public string SenderDescriptor =>
        IsFromPlayer && !string.IsNullOrWhiteSpace(RecipientCallsign)
            ? $"TO {ShortenLabel(RecipientCallsign, 14)}"
            : !string.IsNullOrWhiteSpace(RankOrRole)
            ? ShortenLabel(RankOrRole, 18)
            : !string.IsNullOrWhiteSpace(Designation)
                ? ShortenLabel(Designation, 18)
                : ChannelTag;

    public string HeaderColorHex => Priority switch
    {
        MessagePriority.Flash => "#FF5050",
        MessagePriority.Immediate => "#FF8200",
        _ when Channel == RadioChannel.IntelNet => "#7CB8FF",
        _ when Channel == RadioChannel.OpenFreq => "#D8D27A",
        _ when Channel == RadioChannel.AirDefenseNet => "#8CC7FF",
        _ => "#A8E36A"
    };

    public string BodyColorHex => MessageTone switch
    {
        MessageTone.Emergency => "#FFD166",
        MessageTone.Intercepted => "#7CB8FF",
        MessageTone.Hostile => "#FF8A65",
        MessageTone.OpenBroadcast => "#D7E07D",
        MessageTone.Crew => "#B1F18E",
        _ => "#B9D6B3"
    };

    public string AttentionMarker => RequiresAttention ? $"[{PriorityTag}]" : "";

    public bool IsUrgent => Priority == MessagePriority.Flash || Priority == MessagePriority.Immediate;

    private static string ShortenLabel(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value
            .Replace("AIR DEFENSE", "ADF", StringComparison.OrdinalIgnoreCase)
            .Replace("COMMAND", "CMD", StringComparison.OrdinalIgnoreCase)
            .Replace("ACTUAL", "ACT", StringComparison.OrdinalIgnoreCase)
            .Replace("FIRE UNIT", "FIRE", StringComparison.OrdinalIgnoreCase)
            .Replace("BATTERY", "BAT", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..Math.Max(0, maxLength - 1)].TrimEnd() + "…";
    }
}

public class CommManager
{
    private readonly Dictionary<RadioChannel, List<RadioMessage>> _history = new();
    private readonly ConcurrentQueue<RadioMessage> _outgoingQueue = new();

    public event Action<RadioMessage>? MessageReceived;
    public event Action<RadioMessage>? FlashMessageReceived;

    private const int MaxHistoryPerChannel = 200;

    public CommManager()
    {
        foreach (RadioChannel channel in Enum.GetValues<RadioChannel>())
            _history[channel] = new List<RadioMessage>();
    }

    public void Send(RadioMessage message)
    {
        _history[message.Channel].Add(message);
        if (_history[message.Channel].Count > MaxHistoryPerChannel)
            _history[message.Channel].RemoveAt(0);

        GameSessionLogger.Current?.OnRadioMessage(message);
        MessageReceived?.Invoke(message);
        if (message.Priority == MessagePriority.Flash)
            FlashMessageReceived?.Invoke(message);
    }

    public void Queue(RadioMessage message) => _outgoingQueue.Enqueue(message);

    public int ProcessQueue(int maxPerTick = 3)
    {
        int processed = 0;
        while (processed < maxPerTick && _outgoingQueue.TryDequeue(out var message))
        {
            Send(message);
            processed++;
        }

        return processed;
    }

    public RadioMessage SendPlayerMessage(RadioChannel channel, string content, string? recipient = null)
    {
        var speaker = RadioRules.CreatePlayerProfile();
        return SendConstructedMessage(speaker, channel, content, MessagePriority.Routine, MessageType.PlayerMessage, recipient, canReply: false, fromPlayer: true);
    }

    public IReadOnlyList<RadioMessage> GetHistory(RadioChannel channel) => _history[channel].AsReadOnly();

    public IReadOnlyList<RadioMessage> GetAllHistory() =>
        _history.Values.SelectMany(history => history).OrderBy(message => message.Timestamp).ToList();

    public int UnreadCount(RadioChannel channel) => _history[channel].Count(message => !message.IsRead);

    public int TotalUnread => _history.Values.Sum(history => history.Count(message => !message.IsRead));

    public void MarkAllRead(RadioChannel channel)
    {
        foreach (var message in _history[channel])
            message.IsRead = true;
    }

    public static RadioMessage CreateAlliedHQMessage(
        string content,
        MessagePriority priority = MessagePriority.Routine,
        RadioChannel channel = RadioChannel.CommandNet)
    {
        return CreateMessage(
            RadioRules.CreateAlliedHQProfile(),
            channel,
            content,
            priority,
            MessageType.Normal,
            recipient: "ALPHA",
            canReply: true,
            staticLevel: 0.1 + SimulationRandom.Instance.NextDouble() * 0.1);
    }

    public static RadioMessage CreateIntelMessage(string content)
    {
        return CreateMessage(
            RadioRules.CreateIntelProfile(),
            RadioChannel.IntelNet,
            content,
            MessagePriority.Priority,
            MessageType.IntelUpdate,
            recipient: "ALPHA",
            canReply: true,
            staticLevel: 0.2);
    }

    public static RadioMessage CreateCrewMessage(
        string senderName,
        string content,
        MessagePriority priority = MessagePriority.Routine,
        string designation = "ALPHA FIRE UNIT")
    {
        return CreateMessage(
            RadioRules.CreateCrewProfile(senderName, designation),
            RadioChannel.BatteryNet,
            content,
            priority,
            MessageType.RadioChatter,
            canReply: true,
            staticLevel: 0.05);
    }

    public static RadioMessage CreateInterceptMessage(string content, string senderName = "RAVEN ACTUAL")
    {
        return CreateMessage(
            RadioRules.CreateEnemyProfile(senderName, "RAVEN NET", "HOSTILE STRIKE PACKAGE", allowProfanity: true),
            RadioChannel.IntelNet,
            content,
            MessagePriority.Priority,
            MessageType.Intercept,
            canReply: false,
            staticLevel: 0.6);
    }

    public static RadioMessage CreateOpenFrequencyMessage(
        RadioSpeakerProfile speaker,
        string content,
        MessagePriority priority = MessagePriority.Priority,
        string? recipient = null,
        bool canReply = true)
    {
        return CreateMessage(
            speaker,
            RadioChannel.OpenFreq,
            content,
            priority,
            MessageType.RadioChatter,
            recipient,
            canReply,
            staticLevel: 0.3 + SimulationRandom.Instance.NextDouble() * 0.2);
    }

    public static RadioMessage CreateMessage(
        RadioSpeakerProfile speaker,
        RadioChannel channel,
        string content,
        MessagePriority priority,
        MessageType type,
        string? recipient = null,
        bool canReply = false,
        double staticLevel = 0.15,
        string? conversationId = null)
    {
        var directive = RadioRules.PrepareTransmission(speaker, channel, content, priority, type, canReply);
        return new RadioMessage
        {
            Channel = directive.EffectiveChannel,
            ChannelKind = RadioRules.GetChannelKind(directive.EffectiveChannel),
            SenderCallsign = speaker.SenderCallsign,
            RecipientCallsign = recipient,
            Content = directive.SanitizedContent,
            Priority = priority,
            Type = directive.EffectiveType,
            MessageTone = directive.Tone,
            IsFromPlayer = speaker.Affiliation == SpeakerAffiliation.Player,
            StaticLevel = staticLevel,
            SpeakerDisplayName = speaker.SpeakerDisplayName,
            RankOrRole = speaker.RankOrRole,
            UnitCallsign = speaker.UnitCallsign,
            Designation = speaker.Designation,
            PriorityTag = directive.PriorityTag,
            RequiresAttention = directive.RequiresAttention,
            CanReply = directive.CanReply,
            ConversationId = conversationId,
            SpeakerAffiliation = speaker.Affiliation
        };
    }

    private RadioMessage SendConstructedMessage(
        RadioSpeakerProfile speaker,
        RadioChannel channel,
        string content,
        MessagePriority priority,
        MessageType type,
        string? recipient,
        bool canReply,
        bool fromPlayer)
    {
        var message = CreateMessage(speaker, channel, content, priority, type, recipient, canReply, staticLevel: fromPlayer ? 0.0 : 0.15);
        Send(message);
        return message;
    }
}

public static class BrevityCodes
{
    public static readonly Dictionary<string, string> Codes = new()
    {
        ["BOGEY"] = "Unknown radar contact",
        ["BANDIT"] = "Confirmed hostile aircraft",
        ["FRIENDLY"] = "Confirmed friendly aircraft",
        ["NEUTRAL"] = "Neutral aircraft",
        ["SPIKE"] = "Radar warning receiver indication from a threat",
        ["BRAA"] = "Bearing, Range, Altitude, Aspect target information",
        ["BULLSEYE"] = "Reference point (battery position in this context)",
        ["ANGELS"] = "Altitude in thousands of feet",
        ["BUSTER"] = "Fly at maximum speed",
        ["COLD"] = "Aircraft pointing away from threat",
        ["HOT"] = "Aircraft pointing toward threat",
        ["BEAM"] = "Aircraft flying perpendicular to radar line",
        ["DRAG"] = "Aircraft heading away, usually retreating",
        ["SPLASH"] = "Confirmed kill target destroyed",
        ["MUSIC"] = "Radar jamming detected",
        ["VAMPIRE"] = "Confirmed hostile missile inbound",
        ["FOX ONE"] = "Semi-active radar missile fired",
        ["FOX TWO"] = "IR missile fired",
        ["FOX THREE"] = "Active radar missile fired",
        ["MAGNUM"] = "Anti-radiation missile fired",
        ["WILCO"] = "Will comply",
        ["ROGER"] = "Received and understood",
        ["NEGATIVE"] = "No",
        ["AFFIRM"] = "Yes",
        ["BINGO"] = "Minimum fuel state must RTB",
        ["WINCHESTER"] = "Out of weapons",
        ["MAYDAY"] = "Aircraft in distress",
        ["BLIND"] = "No radar contact with friendly",
        ["PICTURE"] = "Provide air situation update",
        ["SKOSH"] = "Missiles at minimum range breaking off",
        ["CEASE FIRE"] = "Stop firing immediately",
        ["HOLD FIRE"] = "Do not fire",
        ["WEAPONS FREE"] = "Fire at all except confirmed friendlies",
        ["WEAPONS TIGHT"] = "Fire only at confirmed hostiles",
        ["WEAPONS HOLD"] = "Do not fire except self-defense"
    };

    public static string GetDefinition(string code) =>
        Codes.TryGetValue(code.ToUpperInvariant(), out var definition) ? definition : "Unknown brevity code";
}
