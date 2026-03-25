namespace DEADSKY.Core.Simulation;

/// <summary>
/// Lightweight pub/sub event bus. All simulation systems communicate
/// through this rather than direct references, keeping them decoupled.
/// Handlers are called synchronously on the simulation thread.
/// UI must dispatch to UI thread in its handlers.
/// </summary>
public class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public void Subscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (!_handlers.TryGetValue(type, out var list))
            _handlers[type] = list = new();
        list.Add(handler);
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (_handlers.TryGetValue(type, out var list))
            list.Remove(handler);
    }

    public void Publish<T>(T eventData)
    {
        var type = typeof(T);
        if (!_handlers.TryGetValue(type, out var list)) return;

        // Snapshot to avoid modification during iteration
        foreach (var handler in list.ToList())
        {
            try { ((Action<T>)handler)(eventData); }
            catch (Exception ex)
            {
                // Don't let one bad handler crash the simulation
                Console.Error.WriteLine($"EventBus handler error for {type.Name}: {ex.Message}");
            }
        }
    }

    public void Clear() => _handlers.Clear();
}

// ── Standard game events ──────────────────────────────────────────────────

public record SimulationTickEvent(double GameTimeSec, double DeltaTime, int TickCount);
public record NewContactEvent(string TrackId, string Classification);
public record TrackDroppedEvent(string TrackId);
public record MissileLaunchedEvent(string MissileId, string TargetTrackId, string LauncherId);
public record EngagementResultEvent(string TrackId, string Result, bool WasKill);
public record AlertLevelChangedEvent(string OldLevel, string NewLevel);
public record ROEChangedEvent(string OldROE, string NewROE, string AuthorizedBy);
public record ReinforcementArrivedEvent(string UnitType, string Callsign);
public record AlliedUnitDestroyedEvent(string Callsign, string UnitType);
public record IntelUpdateEvent(string Content, string Source);
public record WeatherChangedEvent(double VisibilityNm, double PrecipitationMmHr);
public record CrewEventOccurredEvent(string SoldierId, string EventType, string Description);
public record BatteryDamagedEvent(string ComponentDamaged, double SeverityPct);
public record MissionCompletedEvent(bool Victory, string Reason, double MissionTimeSec);
public record ScenarioTriggerEvent(string TriggerName, string TriggerData);
