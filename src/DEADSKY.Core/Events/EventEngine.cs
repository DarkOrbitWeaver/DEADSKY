using DEADSKY.Core.Personnel;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Events;

public enum GameEventCategory
{
    Flavor,
    RadarMalfunction,
    CrewMoment,
    Logistics
}

public sealed class GameEvent
{
    public GameEventCategory Category { get; init; }
    public string Description { get; init; } = "";
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

public sealed class EventEngine
{
    private readonly SimulationEngine _sim;
    private readonly CrewRoster _crew;
    private double _cooldownSec = 45;

    public event Action<GameEvent>? EventFired;

    public EventEngine(SimulationEngine sim, CrewRoster crew)
    {
        _sim = sim;
        _crew = crew;
    }

    public void Update(double deltaTime)
    {
        _cooldownSec -= deltaTime;
        if (_cooldownSec > 0)
            return;

        _cooldownSec = 90;
        if (_sim.LatestSnapshot.HostileTracks.Count >= 4)
        {
            EventFired?.Invoke(new GameEvent
            {
                Category = GameEventCategory.CrewMoment,
                Description = "Crew stress levels rising during multi-axis raid."
            });
        }
    }
}
