using DEADSKY.Core.Campaign;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;

namespace DEADSKY.Backend.Tests;

/// <summary>
/// Tests for Task 4.4: Wire CAP fighter to CommManager.
/// Validates that CAP fighters spawned by FriendlySupportDirector have CommManager properly set.
/// Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8
/// </summary>
public class CapCommManagerWiringTests
{
    [Fact]
    public void FriendlySupportDirector_SpawnCapFighter_SetsCommManager()
    {
        // Arrange
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request CAP support
        var result = director.RequestSupport(
            FriendlySupportType.CombatAirPatrol,
            "ALPHA ACTUAL",
            "Need CAP coverage",
            0);

        Assert.True(result.Accepted);

        // Act - Advance time to spawn CAP fighter
        director.Tick(result.EtaSec + 1, result.EtaSec + 1);

        // Get the spawned CAP fighter
        var capPackage = director.Packages.First(p => p.Type == FriendlySupportType.CombatAirPatrol);
        Assert.NotNull(capPackage.SpawnedEntityId);

        var fighter = entityManager.Get(capPackage.SpawnedEntityId) as Aircraft;

        // Assert
        Assert.NotNull(fighter);
        Assert.NotNull(fighter.CommManager);
        Assert.Same(comms, fighter.CommManager);
    }

    [Fact]
    public void CapFighter_WithCommManager_CanSendOnStationReport()
    {
        // Arrange
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        var result = director.RequestSupport(
            FriendlySupportType.CombatAirPatrol,
            "ALPHA ACTUAL",
            "Need CAP coverage",
            0);
        director.Tick(result.EtaSec + 1, result.EtaSec + 1);

        var capPackage = director.Packages.First(p => p.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.Get(capPackage.SpawnedEntityId!) as Aircraft;
        Assert.NotNull(fighter);

        int messageCount = 0;
        comms.MessageReceived += _ => messageCount++;

        // Act - Update fighter to trigger on-station report
        fighter.Update(1.0);
        comms.ProcessQueue();

        // Assert - Should send on-station report
        Assert.True(messageCount > 0);
    }

    [Fact]
    public void CapFighter_WithCommManager_CanSendWilcoAcknowledgment()
    {
        // Arrange
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        var result = director.RequestSupport(
            FriendlySupportType.CombatAirPatrol,
            "ALPHA ACTUAL",
            "Need CAP coverage",
            0);
        director.Tick(result.EtaSec + 1, result.EtaSec + 1);

        var capPackage = director.Packages.First(p => p.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.Get(capPackage.SpawnedEntityId!) as Aircraft;
        Assert.NotNull(fighter);

        // Clear any messages from spawning process
        comms.ProcessQueue();
        
        int messageCount = 0;
        comms.MessageReceived += _ => messageCount++;

        // Act - Send intercept order
        fighter.SetInterceptTarget("TRACK-1234");
        comms.ProcessQueue();

        // Assert - Should send Wilco acknowledgment
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void CapFighter_WithCommManager_CanSendTallyReport()
    {
        // Arrange
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        var result = director.RequestSupport(
            FriendlySupportType.CombatAirPatrol,
            "ALPHA ACTUAL",
            "Need CAP coverage",
            0);
        director.Tick(result.EtaSec + 1, result.EtaSec + 1);

        var capPackage = director.Packages.First(p => p.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.Get(capPackage.SpawnedEntityId!) as Aircraft;
        Assert.NotNull(fighter);

        // Clear any messages from spawning process
        comms.ProcessQueue();
        
        int messageCount = 0;
        comms.MessageReceived += _ => messageCount++;

        // Act - Send tally report
        fighter.SendTallyReport("TRACK-1234");
        comms.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void CapFighter_WithCommManager_CanSendFox3Report()
    {
        // Arrange
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        var result = director.RequestSupport(
            FriendlySupportType.CombatAirPatrol,
            "ALPHA ACTUAL",
            "Need CAP coverage",
            0);
        director.Tick(result.EtaSec + 1, result.EtaSec + 1);

        var capPackage = director.Packages.First(p => p.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.Get(capPackage.SpawnedEntityId!) as Aircraft;
        Assert.NotNull(fighter);

        // Clear any messages from spawning process
        comms.ProcessQueue();
        
        int messageCount = 0;
        comms.MessageReceived += _ => messageCount++;

        // Act - Send Fox-3 report
        fighter.SendFox3Report("TRACK-1234", WeaponType.Aim120);
        comms.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void CapFighter_WithCommManager_CanSendSplashReport()
    {
        // Arrange
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        var result = director.RequestSupport(
            FriendlySupportType.CombatAirPatrol,
            "ALPHA ACTUAL",
            "Need CAP coverage",
            0);
        director.Tick(result.EtaSec + 1, result.EtaSec + 1);

        var capPackage = director.Packages.First(p => p.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.Get(capPackage.SpawnedEntityId!) as Aircraft;
        Assert.NotNull(fighter);

        // Clear any messages from spawning process
        comms.ProcessQueue();
        
        int messageCount = 0;
        comms.MessageReceived += _ => messageCount++;

        // Act - Send Splash report
        fighter.SendSplashReport("TRACK-1234");
        comms.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void CapFighter_WithCommManager_CanSendBingoFuelReport()
    {
        // Arrange
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        var result = director.RequestSupport(
            FriendlySupportType.CombatAirPatrol,
            "ALPHA ACTUAL",
            "Need CAP coverage",
            0);
        director.Tick(result.EtaSec + 1, result.EtaSec + 1);

        var capPackage = director.Packages.First(p => p.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.Get(capPackage.SpawnedEntityId!) as Aircraft;
        Assert.NotNull(fighter);

        // Clear any messages from spawning process
        comms.ProcessQueue();
        
        int messageCount = 0;
        comms.MessageReceived += _ => messageCount++;

        // Act - Send Bingo fuel report
        fighter.SendBingoFuelReport();
        comms.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void CapFighter_WithCommManager_CanSendWinchesterReport()
    {
        // Arrange
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        var result = director.RequestSupport(
            FriendlySupportType.CombatAirPatrol,
            "ALPHA ACTUAL",
            "Need CAP coverage",
            0);
        director.Tick(result.EtaSec + 1, result.EtaSec + 1);

        var capPackage = director.Packages.First(p => p.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.Get(capPackage.SpawnedEntityId!) as Aircraft;
        Assert.NotNull(fighter);

        // Clear any messages from spawning process
        comms.ProcessQueue();
        
        int messageCount = 0;
        comms.MessageReceived += _ => messageCount++;

        // Act - Send Winchester report
        fighter.SendWinchesterReport();
        comms.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void CapFighter_WithCommManager_CanSendDefensiveReport()
    {
        // Arrange
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        var result = director.RequestSupport(
            FriendlySupportType.CombatAirPatrol,
            "ALPHA ACTUAL",
            "Need CAP coverage",
            0);
        director.Tick(result.EtaSec + 1, result.EtaSec + 1);

        var capPackage = director.Packages.First(p => p.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.Get(capPackage.SpawnedEntityId!) as Aircraft;
        Assert.NotNull(fighter);

        // Clear any messages from spawning process
        comms.ProcessQueue();
        
        int messageCount = 0;
        comms.MessageReceived += _ => messageCount++;

        // Act - Send Defensive report
        fighter.SendDefensiveReport();
        comms.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void CapFighter_RadioMessages_ContainCorrectCallsign()
    {
        // Arrange
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        var result = director.RequestSupport(
            FriendlySupportType.CombatAirPatrol,
            "ALPHA ACTUAL",
            "Need CAP coverage",
            0);
        director.Tick(result.EtaSec + 1, result.EtaSec + 1);

        var capPackage = director.Packages.First(p => p.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.Get(capPackage.SpawnedEntityId!) as Aircraft;
        Assert.NotNull(fighter);

        RadioMessage? receivedMessage = null;
        comms.MessageReceived += msg => receivedMessage = msg;

        // Act - Send any radio message
        fighter.SendWilcoAcknowledgment("test order");
        comms.ProcessQueue();

        // Assert - Message should contain fighter's callsign
        Assert.NotNull(receivedMessage);
        Assert.Equal(capPackage.UnitCallsign, receivedMessage.SenderCallsign);
    }

    [Fact]
    public void CapFighter_RadioMessages_UseCommandNetChannel()
    {
        // Arrange
        var comms = new CommManager();
        var entityManager = new EntityManager();
        var director = new FriendlySupportDirector(comms, entityManager);
        director.InitializeForScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives(), null);

        // Request and spawn CAP
        var result = director.RequestSupport(
            FriendlySupportType.CombatAirPatrol,
            "ALPHA ACTUAL",
            "Need CAP coverage",
            0);
        director.Tick(result.EtaSec + 1, result.EtaSec + 1);

        var capPackage = director.Packages.First(p => p.Type == FriendlySupportType.CombatAirPatrol);
        var fighter = entityManager.Get(capPackage.SpawnedEntityId!) as Aircraft;
        Assert.NotNull(fighter);

        RadioMessage? receivedMessage = null;
        comms.MessageReceived += msg => receivedMessage = msg;

        // Act - Send any radio message
        fighter.SendTallyReport("TRACK-1234");
        comms.ProcessQueue();

        // Assert - Should use CommandNet channel
        Assert.NotNull(receivedMessage);
        Assert.Equal(RadioChannel.CommandNet, receivedMessage.Channel);
    }
}
