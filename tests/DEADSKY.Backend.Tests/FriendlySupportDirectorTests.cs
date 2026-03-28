using DEADSKY.Core.Campaign;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Backend.Tests;

public class FriendlySupportDirectorTests
{
    [Fact]
    public void RequestSupport_PictureRelay_AcceptsAndQueuesFriendlyMessage()
    {
        var comms = new CommManager();
        var director = new FriendlySupportDirector(comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        var result = director.RequestSupport(
            FriendlySupportType.PictureRelay,
            "ALPHA ACTUAL",
            "Need refreshed picture.",
            10);

        var queued = SimulationTestFactory.DrainSingleQueuedMessage(comms);

        Assert.True(result.Accepted);
        Assert.Equal(RadioChannel.IntelNet, queued.Channel);
        Assert.Contains("SABLE", queued.DisplayHeader, StringComparison.OrdinalIgnoreCase);
        Assert.True(queued.CanReply);
    }

    [Fact]
    public void Tick_CombatAirPatrolCompletion_MakesSupportVisible()
    {
        var comms = new CommManager();
        var director = new FriendlySupportDirector(comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);

        Assert.Equal(SupportAvailabilityState.CoolingDown, cap.Availability);
        Assert.True(cap.IsVisibleInPicture);
    }

    [Fact]
    public void RequestSupport_DeclareCell_AcceptsAndQueuesIntelMessage()
    {
        var comms = new CommManager();
        var director = new FriendlySupportDirector(comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        var result = director.RequestSupport(
            FriendlySupportType.DeclarationCell,
            "ALPHA ACTUAL",
            "Need declare on TRK-0001.",
            15);

        var queued = SimulationTestFactory.DrainSingleQueuedMessage(comms);

        Assert.True(result.Accepted);
        Assert.Equal(RadioChannel.IntelNet, queued.Channel);
        Assert.Contains("ORACLE", queued.DisplayHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("declare", queued.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tick_JammingSupportCompletion_MakesSupportVisible()
    {
        var comms = new CommManager();
        var director = new FriendlySupportDirector(comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        director.RequestSupport(FriendlySupportType.JammingSupport, "ALPHA ACTUAL", "Need escort-jam.", 0);
        director.Tick(55, 55);

        var jammer = director.Packages.First(package => package.Type == FriendlySupportType.JammingSupport);

        Assert.Equal(SupportAvailabilityState.CoolingDown, jammer.Availability);
        Assert.True(jammer.IsVisibleInPicture);
    }

    [Fact]
    public void Tick_VisibleSupport_UpdatesTacticalPosition()
    {
        var comms = new CommManager();
        var director = new FriendlySupportDirector(comms);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);
        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        double firstBearing = cap.BearingDeg;
        double firstRange = cap.RangeNm;

        director.Tick(15, 85);

        Assert.True(cap.IsVisibleInPicture);
        Assert.True(cap.AltitudeFt > 10000);
        Assert.True(Math.Abs(cap.BearingDeg - firstBearing) > 0.1 || Math.Abs(cap.RangeNm - firstRange) > 0.1);
    }

    [Fact]
    public void UpdateOperationalContext_RaidPressureAndCriticalIncident_ReducesConfidence_AndAutoShowsSupportActors()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 40, rangeNm: 18);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "MiG-29", bearingDeg: 48, rangeNm: 23);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "Su-25", bearingDeg: 53, rangeNm: 27);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "EA-6", bearingDeg: 58, rangeNm: 34);
        sim.RefreshSnapshot();

        var director = new FriendlySupportDirector(sim.Comms);
        director.InitializeForScenario(scenario, null);
        director.UpdateOperationalContext(
            scenario,
            sim.LatestSnapshot,
            new[]
            {
                new EngagementIncident("blue_on_blue_warning", "Check fire", IncidentSeverity.Critical, DateTime.UtcNow)
            });

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        var awacs = director.Packages.First(package => package.Type == FriendlySupportType.Awacs);

        Assert.True(director.CommandConfidence < 1.0);
        Assert.True(cap.VisibleUntilSec > 0);
        Assert.True(awacs.VisibleUntilSec > 0);
        Assert.Contains("TRUST", director.LiveConsequenceSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequestSupport_HighRiskTaskingCanBeBlocked_WhenConfidenceCollapsed()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        using var sim = SimulationTestFactory.CreateLoadedSimulation(scenario);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "SU-24", bearingDeg: 40, rangeNm: 16);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "MiG-29", bearingDeg: 48, rangeNm: 18);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "Su-25", bearingDeg: 53, rangeNm: 22);
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "EA-6", bearingDeg: 58, rangeNm: 28);
        sim.RefreshSnapshot();

        var director = new FriendlySupportDirector(sim.Comms);
        director.InitializeForScenario(scenario, null);
        director.UpdateOperationalContext(
            scenario,
            sim.LatestSnapshot,
            new[]
            {
                new EngagementIncident("friendly_fire_attempt", "Denied shot on friendly.", IncidentSeverity.Critical, DateTime.UtcNow),
                new EngagementIncident("blue_on_blue_warning", "Check fire", IncidentSeverity.Critical, DateTime.UtcNow)
            });

        var result = director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP now.", 10);

        Assert.False(result.Accepted);
        Assert.Contains("fire-discipline", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Task 2.1: Verifies that FriendlySupportDirector spawns real CAP fighter entities
    /// when CAP support becomes available.
    /// </summary>
    [Fact]
    public void Tick_CombatAirPatrolCompletion_SpawnsRealCapEntity()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request CAP support
        var result = director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        Assert.True(result.Accepted);

        // Get the CAP package
        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        Assert.Null(cap.SpawnedEntityId); // No entity spawned yet

        // Tick until CAP becomes available (70 seconds delay)
        director.Tick(70, 70);

        // Verify CAP entity was spawned
        Assert.NotNull(cap.SpawnedEntityId);
        var spawnedEntity = entityManager.Get(cap.SpawnedEntityId);
        Assert.NotNull(spawnedEntity);
        Assert.IsType<Aircraft>(spawnedEntity);
        
        var fighter = (Aircraft)spawnedEntity;
        Assert.Equal(Affiliation.Friendly, fighter.Affiliation);
        Assert.Equal(cap.UnitCallsign, fighter.CallSign);
        Assert.Equal(4, fighter.Aim120Count);
        Assert.Equal(2, fighter.Aim9Count);
        Assert.Equal(AircraftBehavior.OrbitPatrol, fighter.CurrentBehavior);
    }

    /// <summary>
    /// Task 2.1: Verifies that CAP availability is updated when the spawned entity is destroyed.
    /// </summary>
    [Fact]
    public void Tick_CapEntityDestroyed_UpdatesAvailability()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        Assert.NotNull(cap.SpawnedEntityId);
        Assert.Equal(SupportAvailabilityState.CoolingDown, cap.Availability);

        // Destroy the CAP entity
        entityManager.Destroy(cap.SpawnedEntityId, "Shot down");

        // Tick to update state
        director.Tick(1, 71);

        // Verify availability updated to Damaged
        Assert.Null(cap.SpawnedEntityId);
        Assert.Equal(SupportAvailabilityState.Damaged, cap.Availability);
        Assert.Contains("lost", cap.LastSummary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Task 2.1: Verifies that CAP availability is updated when the fighter goes Winchester.
    /// </summary>
    [Fact]
    public void Tick_CapEntityWinchester_UpdatesAvailability()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        Assert.NotNull(cap.SpawnedEntityId);

        // Expend all weapons
        var fighter = entityManager.GetAs<Aircraft>(cap.SpawnedEntityId);
        Assert.NotNull(fighter);
        fighter.Aim120Count = 0;
        fighter.Aim9Count = 0;

        // Tick to update state
        director.Tick(1, 71);

        // Verify availability updated to CoolingDown
        Assert.Null(cap.SpawnedEntityId);
        Assert.Equal(SupportAvailabilityState.CoolingDown, cap.Availability);
        Assert.Contains("Winchester", cap.LastSummary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Task 2.1: Verifies that CAP availability is updated when the fighter reaches bingo fuel.
    /// </summary>
    [Fact]
    public void Tick_CapEntityBingoFuel_UpdatesAvailability()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        Assert.NotNull(cap.SpawnedEntityId);

        // Set fuel to bingo level
        var fighter = entityManager.GetAs<Aircraft>(cap.SpawnedEntityId);
        Assert.NotNull(fighter);
        fighter.FuelRemainingKg = fighter.BingoFuelKg - 10; // Below bingo

        // Tick to update state
        director.Tick(1, 71);

        // Verify availability updated to CoolingDown
        Assert.Null(cap.SpawnedEntityId);
        Assert.Equal(SupportAvailabilityState.CoolingDown, cap.Availability);
        Assert.Contains("bingo fuel", cap.LastSummary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Task 2.2: Verifies that CAP fighter entity is actually despawned (removed from EntityManager)
    /// when it reaches bingo fuel and RTBs.
    /// </summary>
    [Fact]
    public void Tick_CapEntityBingoFuel_DespawnsEntity()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        string entityId = cap.SpawnedEntityId!;
        Assert.NotNull(entityId);

        // Verify entity exists in EntityManager
        var fighter = entityManager.GetAs<Aircraft>(entityId);
        Assert.NotNull(fighter);

        // Set fuel to bingo level
        fighter.FuelRemainingKg = fighter.BingoFuelKg - 10; // Below bingo

        // Tick to update state
        director.Tick(1, 71);

        // Verify entity was removed from EntityManager (despawned)
        var despawnedEntity = entityManager.Get(entityId);
        Assert.Null(despawnedEntity);
    }

    /// <summary>
    /// Task 2.2: Verifies that CAP fighter entity is actually despawned (removed from EntityManager)
    /// when it goes Winchester and RTBs.
    /// </summary>
    [Fact]
    public void Tick_CapEntityWinchester_DespawnsEntity()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        string entityId = cap.SpawnedEntityId!;
        Assert.NotNull(entityId);

        // Verify entity exists in EntityManager
        var fighter = entityManager.GetAs<Aircraft>(entityId);
        Assert.NotNull(fighter);

        // Expend all weapons
        fighter.Aim120Count = 0;
        fighter.Aim9Count = 0;

        // Tick to update state
        director.Tick(1, 71);

        // Verify entity was removed from EntityManager (despawned)
        var despawnedEntity = entityManager.Get(entityId);
        Assert.Null(despawnedEntity);
    }

    /// <summary>
    /// Task 2.2: Verifies that CAP fighter entity is actually despawned (removed from EntityManager)
    /// when it is destroyed.
    /// </summary>
    [Fact]
    public void Tick_CapEntityDestroyed_DespawnsEntity()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        string entityId = cap.SpawnedEntityId!;
        Assert.NotNull(entityId);

        // Verify entity exists in EntityManager
        var fighter = entityManager.GetAs<Aircraft>(entityId);
        Assert.NotNull(fighter);

        // Destroy the CAP entity
        entityManager.Destroy(entityId, "Shot down");

        // Tick to update state
        director.Tick(1, 71);

        // Verify entity was removed from EntityManager (despawned)
        var despawnedEntity = entityManager.Get(entityId);
        Assert.Null(despawnedEntity);
    }

    /// <summary>
    /// Task 2.2: Verifies that cooldown is properly set after CAP fighter RTBs.
    /// </summary>
    [Fact]
    public void Tick_CapEntityRtb_SetsCooldownPeriod()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        director.RequestSupport(FriendlySupportType.CombatAirPatrol, "ALPHA ACTUAL", "Need CAP.", 0);
        director.Tick(70, 70);

        var cap = director.Packages.First(package => package.Type == FriendlySupportType.CombatAirPatrol);
        Assert.NotNull(cap.SpawnedEntityId);

        // Set fuel to bingo level to trigger RTB
        var fighter = entityManager.GetAs<Aircraft>(cap.SpawnedEntityId);
        Assert.NotNull(fighter);
        fighter.FuelRemainingKg = fighter.BingoFuelKg - 10;

        // Tick to update state
        director.Tick(1, 71);

        // Verify cooldown is set (180 seconds for CAP, minus 1 second from the tick)
        Assert.Equal(SupportAvailabilityState.CoolingDown, cap.Availability);
        Assert.Equal(179.0, cap.CooldownRemainingSec); // 180 - 1 second tick
    }

    /// <summary>
    /// Task 5.3: Verifies that FriendlySupportDirector spawns a real AWACSAircraft entity
    /// when AWACS support becomes available.
    /// </summary>
    [Fact]
    public void Tick_AwacsCompletion_SpawnsRealAwacsEntity()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        var result = director.RequestSupport(FriendlySupportType.Awacs, "ALPHA ACTUAL", "Need picture.", 0);
        Assert.True(result.Accepted);

        var awacsPackage = director.Packages.First(package => package.Type == FriendlySupportType.Awacs);
        Assert.Null(awacsPackage.SpawnedEntityId); // Not spawned yet

        director.Tick(25, 25); // AWACS delay is 25s

        Assert.NotNull(awacsPackage.SpawnedEntityId);
        var spawnedEntity = entityManager.Get(awacsPackage.SpawnedEntityId);
        Assert.NotNull(spawnedEntity);
        Assert.IsType<AWACSAircraft>(spawnedEntity);
        
        var awacs = (AWACSAircraft)spawnedEntity;
        Assert.Equal(Affiliation.Friendly, awacs.Affiliation);
        Assert.Equal(awacsPackage.UnitCallsign, awacs.CallSign);
        Assert.Equal(AircraftBehavior.OrbitPatrol, awacs.CurrentBehavior);
    }

    /// <summary>
    /// Task 5.3: Verifies that AWACS availability is updated to Damaged when the entity is destroyed.
    /// </summary>
    [Fact]
    public void Tick_AwacsEntityDestroyed_UpdatesAvailability()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        director.RequestSupport(FriendlySupportType.Awacs, "ALPHA ACTUAL", "Need picture.", 0);
        director.Tick(25, 25);

        var awacsPackage = director.Packages.First(package => package.Type == FriendlySupportType.Awacs);
        Assert.NotNull(awacsPackage.SpawnedEntityId);
        
        entityManager.Destroy(awacsPackage.SpawnedEntityId, "Shot down");
        director.Tick(1, 26);

        Assert.Null(awacsPackage.SpawnedEntityId);
        Assert.Equal(SupportAvailabilityState.Damaged, awacsPackage.Availability);
        Assert.Contains("lost", awacsPackage.LastSummary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Task 5.3: Verifies that AWACS availability is updated to CoolingDown when the entity hits bingo fuel.
    /// </summary>
    [Fact]
    public void Tick_AwacsEntityBingoFuel_StartsCooldown()
    {
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        director.RequestSupport(FriendlySupportType.Awacs, "ALPHA ACTUAL", "Need picture.", 0);
        director.Tick(25, 25);

        var awacsPackage = director.Packages.First(package => package.Type == FriendlySupportType.Awacs);
        var awacs = entityManager.GetAs<AWACSAircraft>(awacsPackage.SpawnedEntityId!);
        Assert.NotNull(awacs);

        awacs.FuelRemainingKg = awacs.BingoFuelKg - 10;
        director.Tick(1, 26);

        Assert.Null(awacsPackage.SpawnedEntityId);
        Assert.Equal(SupportAvailabilityState.CoolingDown, awacsPackage.Availability);
        Assert.Contains("bingo fuel", awacsPackage.LastSummary, StringComparison.OrdinalIgnoreCase);
    }
}
