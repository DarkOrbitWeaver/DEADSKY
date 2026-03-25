namespace DEADSKY.Core.Simulation;

/// <summary>
/// Manages game time, time scaling, and the simulation clock.
/// Game time is separate from wall-clock time and can be scaled.
/// </summary>
public class SimulationClock
{
    private double _gameTimeSec;
    private DateTime _lastRealTime = DateTime.UtcNow;
    private bool _running;

    public double TimeScale { get; set; } = 1.0;  // 1=realtime, 2=double speed, etc.
    public double GameTimeSec => _gameTimeSec;
    public bool IsRunning => _running;
    public bool IsPaused => !_running;

    // Mission start time (for display)
    public string GameTimeString
    {
        get
        {
            // Start at 05:45 (dawn patrol default — overridden by scenario)
            double totalSec = _missionStartTimeSec + _gameTimeSec;
            int hours = (int)(totalSec / 3600) % 24;
            int minutes = (int)(totalSec / 60) % 60;
            int seconds = (int)totalSec % 60;
            return $"{hours:D2}:{minutes:D2}:{seconds:D2} ZULU";
        }
    }

    private double _missionStartTimeSec = 5 * 3600 + 45 * 60; // 05:45

    public void SetMissionStartTime(int hour, int minute)
    {
        _missionStartTimeSec = hour * 3600 + minute * 60;
    }

    public void Start()
    {
        _running = true;
        _lastRealTime = DateTime.UtcNow;
    }

    public void Pause() => _running = false;
    public void Resume() { _running = true; _lastRealTime = DateTime.UtcNow; }
    public void Reset() { _gameTimeSec = 0; _running = false; }

    /// <summary>
    /// Get the scaled delta time since last call.
    /// Returns 0 if paused.
    /// Call this at the start of each simulation tick.
    /// </summary>
    public double GetDeltaTime()
    {
        if (!_running) return 0;

        var now = DateTime.UtcNow;
        double realDelta = (now - _lastRealTime).TotalSeconds;
        _lastRealTime = now;

        // Cap delta to avoid spiral of death
        realDelta = Math.Min(realDelta, 0.1);

        double scaledDelta = realDelta * TimeScale;
        _gameTimeSec += scaledDelta;
        return scaledDelta;
    }

    public TimeSpan GameTimeSpan => TimeSpan.FromSeconds(_gameTimeSec);

    public string ElapsedString
    {
        get
        {
            int hours = (int)(_gameTimeSec / 3600);
            int minutes = (int)(_gameTimeSec / 60) % 60;
            int seconds = (int)_gameTimeSec % 60;
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }
    }
}
