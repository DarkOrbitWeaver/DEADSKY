using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;

namespace DEADSKY.Backend.Tests;

public class AWACSRadarFusionTests
{
    /// <summary>
    /// Task 5.4: Verify RadarSystem applies the 1.3x range multiplier when AWACS is active.
    /// </summary>
    [Fact]
    public void RadarSystem_WithAwacsActive_AppliesRangeMultiplier()
    {
        var radar = new RadarSystem();
        radar.Model.IsOnline = true;
        radar.Mode = RadarMode.Search;
        
        // Add AWACS
        var awacs = new AWACSAircraft { Affiliation = Affiliation.Friendly };
        var entities = new List<Entity> { awacs };
        
        radar.Update(1.0, entities);
        
        Assert.Equal(1.3, radar.AwacRangeMultiplier);
    }

    /// <summary>
    /// Task 5.4: Verify RadarSystem uses the base 1.0x range multiplier when no AWACS is active.
    /// </summary>
    [Fact]
    public void RadarSystem_WithoutAwacs_UsesBaseRange()
    {
        var radar = new RadarSystem();
        radar.Model.IsOnline = true;
        radar.Mode = RadarMode.Search;
        
        // No AWACS, just a normal fighter
        var fighter = new Aircraft { Affiliation = Affiliation.Friendly, Role = AircraftRole.Fighter };
        var entities = new List<Entity> { fighter };
        
        radar.Update(1.0, entities);
        
        Assert.Equal(1.0, radar.AwacRangeMultiplier);
    }
}
