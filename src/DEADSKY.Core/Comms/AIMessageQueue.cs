using DEADSKY.Core.Events;

namespace DEADSKY.Core.Comms;

/// <summary>
/// Represents a queued AI message with associated event and metadata.
/// </summary>
public sealed record QueuedMessage
{
    public AIGameEvent Event { get; init; } = null!;
    public string AgentName { get; init; } = "";
    public string MessageContent { get; init; } = "";
    public DateTime QueuedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Manages a priority queue for AI messages with staleness detection.
/// Urgent messages are prioritized over routine messages, and stale routine messages are discarded.
/// </summary>
public sealed class AIMessageQueue
{
    private readonly List<QueuedMessage> _urgentQueue = new();
    private readonly List<QueuedMessage> _routineQueue = new();
    private readonly Dictionary<string, DateTime> _recentMessages = new();

    private const double StaleThresholdSeconds = 60.0;
    private const double DeduplicationWindowSeconds = 30.0;

    /// <summary>
    /// Enqueues a message based on the event priority.
    /// Deduplicates messages within a 30-second window.
    /// </summary>
    /// <param name="gameEvent">The game event that triggered the message.</param>
    /// <param name="agentName">The name of the AI agent sending the message.</param>
    /// <param name="messageContent">The content of the message.</param>
    public void EnqueueMessage(AIGameEvent gameEvent, string agentName, string messageContent)
    {
        // Create deduplication key from agent + event type
        string dedupKey = $"{agentName}:{gameEvent.EventType}";
        var now = DateTime.UtcNow;
        
        // Check if we recently sent this type of message
        if (_recentMessages.TryGetValue(dedupKey, out var lastSent))
        {
            if ((now - lastSent).TotalSeconds < DeduplicationWindowSeconds)
            {
                // Skip duplicate message
                return;
            }
        }
        
        // Update deduplication tracker
        _recentMessages[dedupKey] = now;
        
        // Clean old entries from deduplication tracker
        var keysToRemove = _recentMessages
            .Where(kvp => (now - kvp.Value).TotalSeconds > DeduplicationWindowSeconds * 2)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in keysToRemove)
            _recentMessages.Remove(key);
        
        var queuedMessage = new QueuedMessage
        {
            Event = gameEvent,
            AgentName = agentName,
            MessageContent = messageContent,
            QueuedAt = now
        };

        if (gameEvent.Priority == EventPriority.Urgent)
        {
            _urgentQueue.Add(queuedMessage);
        }
        else
        {
            _routineQueue.Add(queuedMessage);
        }
    }

    /// <summary>
    /// Dequeues the next message from the queue, prioritizing urgent messages.
    /// Returns null if no messages are available.
    /// </summary>
    /// <returns>The next queued message, or null if the queue is empty.</returns>
    public QueuedMessage? DequeueNextMessage()
    {
        // Prioritize urgent messages
        if (_urgentQueue.Count > 0)
        {
            var message = _urgentQueue[0];
            _urgentQueue.RemoveAt(0);
            return message;
        }

        // Fall back to routine messages
        if (_routineQueue.Count > 0)
        {
            var message = _routineQueue[0];
            _routineQueue.RemoveAt(0);
            return message;
        }

        return null;
    }

    /// <summary>
    /// Discards routine messages that are older than the stale threshold (60 seconds).
    /// Urgent messages are never discarded.
    /// </summary>
    /// <returns>The number of stale messages discarded.</returns>
    public int DiscardStaleMessages()
    {
        var now = DateTime.UtcNow;
        var initialCount = _routineQueue.Count;

        _routineQueue.RemoveAll(msg =>
            (now - msg.QueuedAt).TotalSeconds > StaleThresholdSeconds);

        return initialCount - _routineQueue.Count;
    }

    /// <summary>
    /// Gets the total number of messages in the queue (urgent + routine).
    /// </summary>
    public int Count => _urgentQueue.Count + _routineQueue.Count;

    /// <summary>
    /// Gets the number of urgent messages in the queue.
    /// </summary>
    public int UrgentCount => _urgentQueue.Count;

    /// <summary>
    /// Gets the number of routine messages in the queue.
    /// </summary>
    public int RoutineCount => _routineQueue.Count;
}
