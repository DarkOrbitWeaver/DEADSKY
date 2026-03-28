using DEADSKY.Core.Campaign;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;

namespace DEADSKY.Backend.Tests;

public class AirbaseManagerTests
{
    /// <summary>
    /// Requirement 6.2: Ensure RequestScramble deducts resources and queues the launch request.
    /// </summary>
    [Fact]
    public void RequestScramble_WithResources_QueuesLaunchAndDeductsResources()
    {
        var entities = new EntityManager();
        var comms = new CommManager();
        var manager = new AirbaseManager(entities, comms);

        var airbase = new Airbase 
        { 
            CallSign = "ALPHA", 
            FightersAvailable = 10,
            FuelAvailableKg = 200000,
            AamAvailable = 50
        };
        entities.Add(airbase);

        var result = manager.RequestScramble("ALPHA", AircraftRole.Fighter, 2);

        Assert.True(result.Accepted);
        Assert.Equal(8, airbase.FightersAvailable);
        Assert.Equal(200000 - (2 * 15000), airbase.FuelAvailableKg);
        Assert.Equal(50 - (2 * 6), airbase.AamAvailable);
        
        var queue = manager.GetQueue();
        Assert.Single(queue);
        Assert.Equal(airbase.Id, queue[0].AirbaseId);
        Assert.Equal(2, queue[0].Count);
    }

    /// <summary>
    /// Requirement 6.2: Ensure RequestScramble fails if insufficient aircraft.
    /// </summary>
    [Fact]
    public void RequestScramble_WithoutSufficientFighters_FailsAndDoesNotQueue()
    {
        var entities = new EntityManager();
        var comms = new CommManager();
        var manager = new AirbaseManager(entities, comms);

        var airbase = new Airbase 
        { 
            CallSign = "BETA", 
            FightersAvailable = 1 // Not enough
        };
        entities.Add(airbase);

        var result = manager.RequestScramble("BETA", AircraftRole.Fighter, 2);

        Assert.False(result.Accepted);
        Assert.Contains("Insufficient fighters", result.Reason);
        Assert.Equal(1, airbase.FightersAvailable); // Unchanged
        
        Assert.Empty(manager.GetQueue());
    }

    /// <summary>
    /// Requirement 6.3: Tick advances time, completes the scramble, removes it from queue, and physically spawns aircraft.
    /// </summary>
    [Fact]
    public void Tick_CompletesScramble_SpawnsEntity()
    {
        var entities = new EntityManager();
        var comms = new CommManager();
        var manager = new AirbaseManager(entities, comms);

        var airbase = new Airbase 
        { 
            CallSign = "CHARLIE", 
            FightersAvailable = 4
        };
        entities.Add(airbase);

        var result = manager.RequestScramble("CHARLIE", AircraftRole.Fighter, 2);
        Assert.True(result.Accepted);
        
        var queue = manager.GetQueue();
        var delay = queue[0].DelayRemainingSec;

        // Tick precisely near the end
        manager.Tick(delay - 1.0);
        Assert.Single(manager.GetQueue()); // Still in queue

        // Tick past the deadline
        manager.Tick(1.1);

        // Queue should be empty
        Assert.Empty(manager.GetQueue());

        // We should have 2 new fighters + 1 airbase
        Assert.Equal(3, entities.GetSnapshot().Count);
        
        var fighters = entities.GetByType<Aircraft>();
        Assert.Equal(2, fighters.Count);
        Assert.True(fighters.All(f => f.Role == AircraftRole.Fighter));
        Assert.True(fighters.All(f => f.CurrentBehavior == AircraftBehavior.IngressAttack));
        Assert.True(fighters.All(f => f.CallSign.StartsWith("FLIGHT-")));
    }
}
