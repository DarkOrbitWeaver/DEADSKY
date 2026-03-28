namespace DEADSKY.Core.Comms;

/// <summary>
/// Manages cooldown timers for AI-generated messages to prevent spam.
/// Enforces per-agent cooldown (30 seconds) and global cooldown (10 seconds).
/// </summary>
public sealed class AIMessageCooldownManager
{
    private readonly Dictionary<string, DateTime> _agentLastMessageTime = new();
    private DateTime _globalLastMessageTime = DateTime.MinValue;

    private const double PerAgentCooldownSeconds = 30.0;
    private const double GlobalCooldownSeconds = 10.0;

    /// <summary>
    /// Checks if the specified agent can send a message based on cooldown rules.
    /// </summary>
    /// <param name="agentName">The name of the agent attempting to send a message.</param>
    /// <returns>True if the agent can send a message; otherwise, false.</returns>
    public bool CanSendMessage(string agentName)
    {
        var now = DateTime.UtcNow;

        // Check global cooldown
        if ((now - _globalLastMessageTime).TotalSeconds < GlobalCooldownSeconds)
            return false;

        // Check per-agent cooldown
        if (_agentLastMessageTime.TryGetValue(agentName, out var lastTime))
        {
            if ((now - lastTime).TotalSeconds < PerAgentCooldownSeconds)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Records that the specified agent has sent a message, updating cooldown timers.
    /// </summary>
    /// <param name="agentName">The name of the agent that sent a message.</param>
    public void RecordMessage(string agentName)
    {
        var now = DateTime.UtcNow;
        _agentLastMessageTime[agentName] = now;
        _globalLastMessageTime = now;
    }

    /// <summary>
    /// Resets all cooldown timers, allowing immediate AI responses.
    /// Typically called when the player sends a message.
    /// </summary>
    public void ResetCooldowns()
    {
        _agentLastMessageTime.Clear();
        _globalLastMessageTime = DateTime.MinValue;
    }
}
