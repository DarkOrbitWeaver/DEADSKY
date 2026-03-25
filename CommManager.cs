namespace DEADSKY.Core.Comms;

public enum RadioChannel
{
    CommandNet = 1,
    BatteryNet = 2,
    AirDefenseNet = 3,
    IntelNet = 4,
    Guard = 5,          // 243.0 MHz emergency
    OpenFreq = 6        // 121.5 MHz
}

public enum MessagePriority
{
    Flash = 4,       // Red, full-screen alarm
    Immediate = 3,   // Orange, alert sound
    Priority = 2,    // Yellow, notification
    Routine = 1      // Normal
}

public enum MessageType
{
    Normal,
    StatusReport,
    IntelUpdate,
    Alert,
    PlayerMessage,
    RadioChatter,
    Intercept    // Overheard enemy comms
}

public record RadioMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public RadioChannel Channel { get; init; }
    public string SenderCallsign { get; init; } = "";
    public string? RecipientCallsign { get; init; }
    public string Content { get; init; } = "";
    public MessagePriority Priority { get; init; } = MessagePriority.Routine;
    public MessageType Type { get; init; } = MessageType.Normal;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsFromPlayer { get; init; }
    public bool IsRead { get; set; }

    // For radio effect: static level (0=clear, 1=heavy static)
    public double StaticLevel { get; init; } = 0.15;

    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
    public string DisplayHeader => RecipientCallsign != null
        ? $"{SenderCallsign} → {RecipientCallsign}"
        : SenderCallsign;
}

/// <summary>
/// Routes radio messages between channels, maintains history,
/// and queues outgoing AI messages.
/// </summary>
public class CommManager
{
    // Message history per channel
    private readonly Dictionary<RadioChannel, List<RadioMessage>> _history = new();

    // Pending outgoing messages (from AI agents)
    private readonly Queue<RadioMessage> _outgoingQueue = new();

    // Events
    public event Action<RadioMessage>? MessageReceived;
    public event Action<RadioMessage>? FlashMessageReceived; // High priority alert

    private const int MaxHistoryPerChannel = 200;

    public CommManager()
    {
        foreach (RadioChannel ch in Enum.GetValues<RadioChannel>())
            _history[ch] = new List<RadioMessage>();
    }

    // ── Send ──────────────────────────────────────────────────────────

    /// <summary>Deliver a message immediately</summary>
    public void Send(RadioMessage message)
    {
        _history[message.Channel].Add(message);
        if (_history[message.Channel].Count > MaxHistoryPerChannel)
            _history[message.Channel].RemoveAt(0);

        MessageReceived?.Invoke(message);
        if (message.Priority == MessagePriority.Flash)
            FlashMessageReceived?.Invoke(message);
    }

    /// <summary>Queue a message to be delivered on next tick (for AI-generated messages)</summary>
    public void Queue(RadioMessage message)
    {
        _outgoingQueue.Enqueue(message);
    }

    /// <summary>Process queued messages — call each simulation tick</summary>
    public void ProcessQueue(int maxPerTick = 3)
    {
        int processed = 0;
        while (_outgoingQueue.Count > 0 && processed < maxPerTick)
        {
            var msg = _outgoingQueue.Dequeue();
            Send(msg);
            processed++;
        }
    }

    // ── Player message ────────────────────────────────────────────────

    public RadioMessage SendPlayerMessage(RadioChannel channel, string content, string? recipient = null)
    {
        var msg = new RadioMessage
        {
            Channel = channel,
            SenderCallsign = "ALPHA ACTUAL",
            RecipientCallsign = recipient,
            Content = content,
            Priority = MessagePriority.Routine,
            Type = MessageType.PlayerMessage,
            IsFromPlayer = true,
            StaticLevel = 0.0 // Player's own messages are clear
        };
        Send(msg);
        return msg;
    }

    // ── Query ─────────────────────────────────────────────────────────

    public IReadOnlyList<RadioMessage> GetHistory(RadioChannel channel) =>
        _history[channel].AsReadOnly();

    public IReadOnlyList<RadioMessage> GetAllHistory() =>
        _history.Values.SelectMany(h => h).OrderBy(m => m.Timestamp).ToList();

    public int UnreadCount(RadioChannel channel) =>
        _history[channel].Count(m => !m.IsRead);

    public int TotalUnread =>
        _history.Values.Sum(h => h.Count(m => !m.IsRead));

    public void MarkAllRead(RadioChannel channel)
    {
        foreach (var msg in _history[channel])
            msg.IsRead = true;
    }

    // ── Factory helpers ───────────────────────────────────────────────

    public static RadioMessage CreateAlliedHQMessage(
        string content, MessagePriority priority = MessagePriority.Routine,
        RadioChannel channel = RadioChannel.CommandNet)
    {
        return new RadioMessage
        {
            Channel = channel,
            SenderCallsign = "ECHO ACTUAL",
            RecipientCallsign = "ALPHA",
            Content = content,
            Priority = priority,
            Type = MessageType.Normal,
            StaticLevel = 0.1 + SimulationRandom.Instance.NextDouble() * 0.1
        };
    }

    public static RadioMessage CreateIntelMessage(string content)
    {
        return new RadioMessage
        {
            Channel = RadioChannel.IntelNet,
            SenderCallsign = "INTEL-1",
            RecipientCallsign = "ALPHA",
            Content = content,
            Priority = MessagePriority.Priority,
            Type = MessageType.IntelUpdate,
            StaticLevel = 0.2
        };
    }

    public static RadioMessage CreateCrewMessage(string senderName, string content,
        MessagePriority priority = MessagePriority.Routine)
    {
        return new RadioMessage
        {
            Channel = RadioChannel.BatteryNet,
            SenderCallsign = senderName,
            Content = content,
            Priority = priority,
            Type = MessageType.RadioChatter,
            StaticLevel = 0.05
        };
    }

    public static RadioMessage CreateInterceptMessage(string content)
    {
        return new RadioMessage
        {
            Channel = RadioChannel.IntelNet,
            SenderCallsign = "SIGINT",
            Content = $"[INTERCEPT] {content}",
            Priority = MessagePriority.Priority,
            Type = MessageType.Intercept,
            StaticLevel = 0.6 // Heavy static — intercepted signal
        };
    }
}

/// <summary>
/// NATO brevity codes dictionary — used by AI to generate realistic radio messages
/// </summary>
public static class BrevityCodes
{
    public static readonly Dictionary<string, string> Codes = new()
    {
        ["BOGEY"] = "Unknown radar contact",
        ["BANDIT"] = "Confirmed hostile aircraft",
        ["FRIENDLY"] = "Confirmed friendly aircraft",
        ["NEUTRAL"] = "Neutral aircraft",
        ["SPIKE"] = "Radar warning receiver indication from a threat",
        ["BRAA"] = "Bearing, Range, Altitude, Aspect — target information",
        ["BULLSEYE"] = "Reference point (battery position in this context)",
        ["ANGELS"] = "Altitude in thousands of feet (ANGELS 18 = 18,000ft)",
        ["BUSTER"] = "Fly at maximum speed",
        ["COLD"] = "Aircraft pointing away from threat",
        ["HOT"] = "Aircraft pointing toward threat (nose-on)",
        ["BEAM"] = "Aircraft flying perpendicular to radar line",
        ["DRAG"] = "Aircraft heading away (tail-on), usually retreating",
        ["SPLASH"] = "Confirmed kill — target destroyed",
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
        ["BINGO"] = "Minimum fuel state — must RTB",
        ["WINCHESTER"] = "Out of weapons",
        ["MAYDAY"] = "Aircraft in distress",
        ["BLIND"] = "No radar contact with friendly",
        ["PICTURE"] = "Provide air situation update",
        ["SKOSH"] = "Missiles at minimum range — breaking off",
        ["CEASE FIRE"] = "Stop firing immediately",
        ["HOLD FIRE"] = "Do not fire",
        ["WEAPONS FREE"] = "Fire at all except confirmed friendlies",
        ["WEAPONS TIGHT"] = "Fire only at confirmed hostiles",
        ["WEAPONS HOLD"] = "Do not fire except self-defense",
    };

    public static string GetDefinition(string code) =>
        Codes.TryGetValue(code.ToUpperInvariant(), out var def) ? def : "Unknown brevity code";
}

// Reference SimulationRandom from Entities namespace
using DEADSKY.Core.Entities;
