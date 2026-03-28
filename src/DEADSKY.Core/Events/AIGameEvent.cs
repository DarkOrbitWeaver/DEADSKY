namespace DEADSKY.Core.Events;

/// <summary>
/// Defines the priority level for AI game events.
/// </summary>
public enum EventPriority
{
    /// <summary>
    /// Routine messages (acknowledgments, status updates).
    /// </summary>
    Routine,

    /// <summary>
    /// Urgent messages (damage, critical threats).
    /// </summary>
    Urgent
}

/// <summary>
/// Represents a game event that can trigger AI communication.
/// </summary>
public sealed class AIGameEvent : IEquatable<AIGameEvent>
{
    /// <summary>
    /// Gets the type of the game event.
    /// </summary>
    public GameEventType EventType { get; init; }

    /// <summary>
    /// Gets the timestamp when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the flexible event data dictionary.
    /// </summary>
    public Dictionary<string, object> EventData { get; init; } = new();

    /// <summary>
    /// Gets the priority level of the event.
    /// </summary>
    public EventPriority Priority { get; init; } = EventPriority.Routine;

    /// <summary>
    /// Determines whether the specified event is equal to the current event.
    /// </summary>
    public bool Equals(AIGameEvent? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return EventType == other.EventType &&
               Timestamp == other.Timestamp &&
               Priority == other.Priority &&
               EventDataEquals(EventData, other.EventData);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current event.
    /// </summary>
    public override bool Equals(object? obj) => Equals(obj as AIGameEvent);

    /// <summary>
    /// Returns the hash code for this event.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EventType);
        hash.Add(Timestamp);
        hash.Add(Priority);
        
        // Add event data keys and values to hash
        foreach (var kvp in EventData.OrderBy(x => x.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }
        
        return hash.ToHashCode();
    }

    /// <summary>
    /// Compares two event data dictionaries for equality.
    /// </summary>
    private static bool EventDataEquals(Dictionary<string, object> dict1, Dictionary<string, object> dict2)
    {
        if (dict1.Count != dict2.Count)
            return false;

        foreach (var kvp in dict1)
        {
            if (!dict2.TryGetValue(kvp.Key, out var value2))
                return false;

            if (!Equals(kvp.Value, value2))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(AIGameEvent? left, AIGameEvent? right)
    {
        if (left is null)
            return right is null;

        return left.Equals(right);
    }

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(AIGameEvent? left, AIGameEvent? right) => !(left == right);
}
