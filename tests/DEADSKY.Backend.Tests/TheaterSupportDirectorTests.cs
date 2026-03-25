using DEADSKY.Core.Campaign;

namespace DEADSKY.Backend.Tests;

public class TheaterSupportDirectorTests
{
    [Fact]
    public void Apply_ReducesReserveMissilesAndRadarRange_WhenSectorIsDamaged()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        var sectorState = new SectorCampaignState
        {
            TheaterName = scenario.SectorMap.TheaterName,
            Objectives = new List<ObjectiveCampaignState>
            {
                new() { ObjectiveId = "depot", ObjectiveName = "Kovran Depot", Importance = "primary", IntegrityPct = 0.62, LastCondition = ObjectiveCondition.Breached },
                new() { ObjectiveId = "relay", ObjectiveName = "Dunewatch Relay", Importance = "secondary", IntegrityPct = 0.74, LastCondition = ObjectiveCondition.UnderAttack }
            }
        };

        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        var battery = sim.Entities.GetPlayerBattery()!;
        double originalRange = battery.RadarRangeNm;
        int originalMissiles = battery.ReserveMissiles;
        double originalPk = battery.MissileSingleShotPk;

        var adjustment = TheaterSupportDirector.Apply(sectorState, scenario, sim);

        Assert.True(battery.ReserveMissiles < originalMissiles);
        Assert.True(battery.RadarRangeNm < originalRange);
        Assert.True(battery.MissileSingleShotPk < originalPk);
        Assert.True(adjustment.CommandNetStrained);
        Assert.Contains("SUPPORT", adjustment.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
