using DEADSKY.Core.Entities;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Backend.Tests;

public class SimulationEngineTests
{
    [Fact]
    public void LoadScenario_AppliesBatteryConfig_AndQueuesOpeningMessage()
    {
        using var sim = new SimulationEngine();
        var scenario = SimulationTestFactory.CreateSingleBogeyScenario();
        var manager = new ScenarioManager(sim);

        manager.LoadScenario(scenario);

        var battery = sim.Entities.GetPlayerBattery();

        Assert.NotNull(battery);
        Assert.Equal(12, battery!.ReserveMissiles);
        Assert.Equal(80, battery.RadarRangeNm);
        Assert.Equal(RulesOfEngagement.WeaponsTight, battery.ROE);
        Assert.Equal(BatteryAlertLevel.Yellow, battery.AlertLevel);

        var opening = SimulationTestFactory.DrainSingleQueuedMessage(sim.Comms);
        Assert.Equal("ECHO ACTUAL", opening.SenderCallsign);
        Assert.Contains("SINGLE BOGEY", opening.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayerFire_OnDetectedHostileTrack_LaunchesMissileAndConsumesLauncher()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var battery = sim.Entities.GetPlayerBattery()!;
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim);

        var fired = sim.PlayerFire(track.TrackId);

        Assert.True(fired, sim.Weapons.LastError);
        Assert.Single(sim.Entities.GetActiveMissiles());
        Assert.Equal(3, battery.ReadyLaunchers);
        Assert.Equal(1, battery.MissilesFired);
        Assert.Equal(RadarMode.SingleTargetTrack, battery.RadarMode);
    }

    [Fact]
    public void PlayerFire_RejectsTrackOutsideEnvelope_AndPreservesLauncherState()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var battery = sim.Entities.GetPlayerBattery()!;
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, rangeNm: 40);

        var fired = sim.PlayerFire(track.TrackId);

        Assert.False(fired);
        Assert.Equal(4, battery.ReadyLaunchers);
        Assert.Equal(0, battery.MissilesFired);
        Assert.Contains("out of range", sim.Weapons.LastError, StringComparison.OrdinalIgnoreCase);
    }
}
