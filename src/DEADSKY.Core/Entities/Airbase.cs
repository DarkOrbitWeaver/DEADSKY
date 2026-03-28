using DEADSKY.Core.Physics;

namespace DEADSKY.Core.Entities;

/// <summary>
/// Requirement 6.1: Airbase class to track aircraft inventory, fuel, and munitions.
/// Represents a physical location on the map that can launch aircraft.
/// </summary>
public class Airbase : Entity
{
    // ── Resources ────────────────────────────────────────────────────────
    
    /// <summary>Number of fighters ready to scramble</summary>
    public int FightersAvailable { get; set; } = 8;
    
    /// <summary>Fuel available for fighters in Kg</summary>
    public double FuelAvailableKg { get; set; } = 500000;
    
    /// <summary>Air-to-Air missiles available</summary>
    public int AamAvailable { get; set; } = 40;

    public Airbase()
    {
        Type = EntityType.Airbase;
        Affiliation = Affiliation.Friendly;
        
        // Airbases don't move
        SpeedMps = 0;
        RequestedSpeedMps = 0;
        AltitudeM = CoordinateSystem.FtToM(0); // Ground level
        RequestedAltitudeM = CoordinateSystem.FtToM(0);
        
        // Large radar signature
        RcsM2 = 1000.0;
        
        FlightModel = new FlightModel
        {
            MaxTurnRateDegSec = 0,
            MaxClimbRateMps = 0,
            MaxDescentRateMps = 0,
            MaxAccelMps2 = 0,
            MaxDecelMps2 = 0,
            MinSpeedMps = 0,
            MaxSpeedMps = 0
        };
    }

    public override void Update(double deltaTime)
    {
        // Airbases are stationary, no physics update needed
        // Just maintain position
    }
}
