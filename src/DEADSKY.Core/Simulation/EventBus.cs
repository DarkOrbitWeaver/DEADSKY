using DEADSKY.Core.Logging;
using System.Diagnostics;

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
        GameLogger.Debug("EVENTBUS", $"Subscribed to {type.Name} (total handlers: {list.Count})");
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (_handlers.TryGetValue(type, out var list))
        {
            list.Remove(handler);
            GameLogger.Debug("EVENTBUS", $"Unsubscribed from {type.Name} (remaining handlers: {list.Count})");
        }
    }

    public void Publish<T>(T eventData)
    {
        var type = typeof(T);
        if (!_handlers.TryGetValue(type, out var list))
        {
            GameLogger.Debug("EVENTBUS", $"Published {type.Name} with no handlers");
            return;
        }

        var handlerCount = list.Count;
        GameLogger.Info("EVENTBUS", $"Publishing {type.Name} to {handlerCount} handler(s): {eventData}");

        var sw = Stopwatch.StartNew();

        // Snapshot to avoid modification during iteration
        var handlers = list.ToList();
        for (int i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i];
            var handlerSw = Stopwatch.StartNew();
            try
            {
                ((Action<T>)handler)(eventData);
                handlerSw.Stop();
                
                if (handlerSw.ElapsedMilliseconds > 10)
                {
                    GameLogger.Warning("EVENTBUS", $"Handler {i + 1}/{handlerCount} for {type.Name} took {handlerSw.ElapsedMilliseconds}ms (slow!)");
                }
                else
                {
                    GameLogger.Debug("EVENTBUS", $"Handler {i + 1}/{handlerCount} for {type.Name} completed in {handlerSw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                handlerSw.Stop();
                // Don't let one bad handler crash the simulation
                GameLogger.Error("EVENTBUS", $"Handler {i + 1}/{handlerCount} for {type.Name} failed after {handlerSw.ElapsedMilliseconds}ms", ex);
            }
        }

        sw.Stop();
        GameLogger.Info("EVENTBUS", $"Completed {type.Name} publish in {sw.ElapsedMilliseconds}ms (all {handlerCount} handlers)");
    }

    public void Clear()
    {
        GameLogger.Info("EVENTBUS", "Clearing all event handlers");
        _handlers.Clear();
    }
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
