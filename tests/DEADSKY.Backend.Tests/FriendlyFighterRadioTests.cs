using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Comms;

namespace DEADSKY.Backend.Tests;

/// <summary>
/// Tests for CAP fighter radio communications.
/// Validates Task 3.1 from real-support-entities spec.
/// Requirements: 2.1, 2.7
/// </summary>
public class FriendlyFighterRadioTests
{
    [Fact]
    public void Aircraft_SendWilcoAcknowledgment_SendsMessageToCommManager()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendWilcoAcknowledgment("intercept TRACK-1234");
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void Aircraft_SendWilcoAcknowledgment_ContainsWilcoAndTaskDescription()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendWilcoAcknowledgment("intercept TRACK-1234");
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Contains("WILCO", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intercept TRACK-1234", receivedMessage.Content);
    }

    [Fact]
    public void Aircraft_SendWilcoAcknowledgment_UsesCommandNetChannel()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendWilcoAcknowledgment("patrol SECTOR-ALPHA");
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(RadioChannel.CommandNet, receivedMessage.Channel);
    }

    [Fact]
    public void Aircraft_SendWilcoAcknowledgment_IncludesCallsign()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendWilcoAcknowledgment("patrol SECTOR-ALPHA");
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal("VIPER-1", receivedMessage.SenderCallsign);
    }

    [Fact]
    public void Aircraft_SendWilcoAcknowledgment_NoCommManager_DoesNotCrash()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.CommManager = null;

        // Act & Assert - Should not throw
        fighter.SendWilcoAcknowledgment("patrol SECTOR-ALPHA");
    }

    [Fact]
    public void Aircraft_SendWilcoAcknowledgment_NoCallsign_DoesNotSend()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = null;
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendWilcoAcknowledgment("patrol SECTOR-ALPHA");
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(0, messageCount);
    }

    [Fact]
    public void Aircraft_OnStationReport_SentWhenEnteringSector()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.PatrolSectorId = "SECTOR-ALPHA";
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        fighter.Position = sectorCenter; // Start at center (within sector)
        fighter.IsOnStation = false;
        fighter.SyncPhysicsState();

        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.Update(1.0);
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
        Assert.True(fighter.IsOnStation);
    }

    [Fact]
    public void Aircraft_OnStationReport_ContainsSectorId()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.PatrolSectorId = "SECTOR-ALPHA";
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        fighter.Position = sectorCenter;
        fighter.IsOnStation = false;
        fighter.SyncPhysicsState();

        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.Update(1.0);
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Contains("SECTOR-ALPHA", receivedMessage.Content);
        Assert.Contains("on station", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aircraft_OnStationReport_SentOnlyOnce()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.PatrolSectorId = "SECTOR-ALPHA";
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        fighter.Position = sectorCenter;
        fighter.IsOnStation = false;
        fighter.SyncPhysicsState();

        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act - Update multiple times
        fighter.Update(1.0);
        commManager.ProcessQueue();
        fighter.Update(1.0);
        commManager.ProcessQueue();
        fighter.Update(1.0);
        commManager.ProcessQueue();

        // Assert - Should only send once
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void Aircraft_OnStationReport_NotSentWithoutCommManager()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.PatrolSectorId = "SECTOR-ALPHA";
        var sectorCenter = new Vec2(50000, 30000);
        double sectorRadius = CoordinateSystem.NmToMeters(20);
        fighter.PatrolSectorCenter = sectorCenter;
        fighter.PatrolSectorRadiusM = sectorRadius;
        fighter.CurrentBehavior = AircraftBehavior.OrbitPatrol;
        fighter.Position = sectorCenter;
        fighter.IsOnStation = false;
        fighter.SyncPhysicsState();
        fighter.CommManager = null;

        // Act & Assert - Should not crash
        fighter.Update(1.0);
        Assert.True(fighter.IsOnStation); // Still sets on-station flag
    }

    [Fact]
    public void Aircraft_SendTallyReport_SendsMessageToCommManager()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void Aircraft_SendTallyReport_ContainsTallyAndTargetId()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Contains("TALLY", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TRACK-1234", receivedMessage.Content);
    }

    [Fact]
    public void Aircraft_SendTallyReport_UsesPriorityLevel()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(MessagePriority.Priority, receivedMessage.Priority);
    }

    [Fact]
    public void Aircraft_SendTallyReport_SentOnlyOncePerTarget()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act - Send tally multiple times for same target
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();

        // Assert - Should only send once
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void Aircraft_SendTallyReport_ResetWhenTargetChanges()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act - Send tally for first target
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();

        // Send tally for different target
        fighter.SendTallyReport("TRACK-5678");
        commManager.ProcessQueue();

        // Assert - Should send twice (once per target)
        Assert.Equal(2, messageCount);
    }

    [Fact]
    public void Aircraft_SendTallyReport_NoCommManager_DoesNotCrash()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.CommManager = null;

        // Act & Assert - Should not throw
        fighter.SendTallyReport("TRACK-1234");
    }

    [Fact]
    public void Aircraft_SendTallyReport_EmptyTargetId_DoesNotSend()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendTallyReport("");
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(0, messageCount);
    }

    [Fact]
    public void Aircraft_ResetTallyReport_AllowsNewTallyForSameTarget()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.InterceptTargetTrackId = "TRACK-1234";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act - Send tally, reset, send again
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();
        
        fighter.ResetTallyReport();
        
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();

        // Assert - Should send twice
        Assert.Equal(2, messageCount);
    }

    [Fact]
    public void Aircraft_RadioMessages_UseFriendlySupportAffiliation()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendWilcoAcknowledgment("patrol SECTOR-ALPHA");
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(SpeakerAffiliation.FriendlySupport, receivedMessage.SpeakerAffiliation);
    }

    [Fact]
    public void Aircraft_RadioMessages_IncludeDesignation()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendTallyReport("TRACK-1234");
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal("F-16C VIPER", receivedMessage.Designation);
    }

    // ── Task 3.2: Engagement Radio Communications Tests ───────────────

    [Fact]
    public void Aircraft_SendFox3Report_SendsMessageToCommManager()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendFox3Report("TRACK-1234", WeaponType.Aim120);
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void Aircraft_SendFox3Report_ContainsFox3AndTargetId()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendFox3Report("TRACK-1234", WeaponType.Aim120);
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Contains("FOX-3", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TRACK-1234", receivedMessage.Content);
    }

    [Fact]
    public void Aircraft_SendFox3Report_Aim9UsesFox2Call()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendFox3Report("TRACK-5678", WeaponType.Aim9);
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Contains("FOX-2", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TRACK-5678", receivedMessage.Content);
    }

    [Fact]
    public void Aircraft_SendFox3Report_UsesPriorityLevel()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendFox3Report("TRACK-1234", WeaponType.Aim120);
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(MessagePriority.Priority, receivedMessage.Priority);
    }

    [Fact]
    public void Aircraft_SendFox3Report_NoCommManager_DoesNotCrash()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.CommManager = null;

        // Act & Assert - Should not throw
        fighter.SendFox3Report("TRACK-1234", WeaponType.Aim120);
    }

    [Fact]
    public void Aircraft_SendFox3Report_EmptyTargetId_DoesNotSend()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendFox3Report("", WeaponType.Aim120);
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(0, messageCount);
    }

    [Fact]
    public void Aircraft_SendSplashReport_SendsMessageToCommManager()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendSplashReport("TRACK-1234");
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void Aircraft_SendSplashReport_ContainsSplashAndTargetId()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendSplashReport("TRACK-1234");
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Contains("SPLASH", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TRACK-1234", receivedMessage.Content);
        Assert.Contains("destroyed", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aircraft_SendSplashReport_UsesPriorityLevel()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendSplashReport("TRACK-1234");
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(MessagePriority.Priority, receivedMessage.Priority);
    }

    [Fact]
    public void Aircraft_SendSplashReport_NoCommManager_DoesNotCrash()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.CommManager = null;

        // Act & Assert - Should not throw
        fighter.SendSplashReport("TRACK-1234");
    }

    [Fact]
    public void Aircraft_SendSplashReport_EmptyTargetId_DoesNotSend()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendSplashReport("");
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(0, messageCount);
    }

    [Fact]
    public void Aircraft_SendBingoFuelReport_SendsMessageToCommManager()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendBingoFuelReport();
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void Aircraft_SendBingoFuelReport_ContainsBingoAndRTB()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendBingoFuelReport();
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Contains("BINGO", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RTB", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aircraft_SendBingoFuelReport_UsesPriorityLevel()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendBingoFuelReport();
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(MessagePriority.Priority, receivedMessage.Priority);
    }

    [Fact]
    public void Aircraft_SendBingoFuelReport_NoCommManager_DoesNotCrash()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.CommManager = null;

        // Act & Assert - Should not throw
        fighter.SendBingoFuelReport();
    }

    [Fact]
    public void Aircraft_SendBingoFuelReport_NoCallsign_DoesNotSend()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = null;
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendBingoFuelReport();
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(0, messageCount);
    }

    [Fact]
    public void Aircraft_SendWinchesterReport_SendsMessageToCommManager()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendWinchesterReport();
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void Aircraft_SendWinchesterReport_ContainsWinchesterAndRTB()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendWinchesterReport();
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Contains("WINCHESTER", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RTB", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aircraft_SendWinchesterReport_UsesPriorityLevel()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendWinchesterReport();
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(MessagePriority.Priority, receivedMessage.Priority);
    }

    [Fact]
    public void Aircraft_SendWinchesterReport_NoCommManager_DoesNotCrash()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.CommManager = null;

        // Act & Assert - Should not throw
        fighter.SendWinchesterReport();
    }

    [Fact]
    public void Aircraft_SendWinchesterReport_NoCallsign_DoesNotSend()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = null;
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendWinchesterReport();
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(0, messageCount);
    }

    [Fact]
    public void Aircraft_SendDefensiveReport_SendsMessageToCommManager()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendDefensiveReport();
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public void Aircraft_SendDefensiveReport_ContainsDefensive()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendDefensiveReport();
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Contains("DEFENSIVE", receivedMessage.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aircraft_SendDefensiveReport_UsesFlashPriority()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendDefensiveReport();
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(MessagePriority.Flash, receivedMessage.Priority);
    }

    [Fact]
    public void Aircraft_SendDefensiveReport_NoCommManager_DoesNotCrash()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        fighter.CommManager = null;

        // Act & Assert - Should not throw
        fighter.SendDefensiveReport();
    }

    [Fact]
    public void Aircraft_SendDefensiveReport_NoCallsign_DoesNotSend()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = null;
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        int messageCount = 0;
        commManager.MessageReceived += _ => messageCount++;

        // Act
        fighter.SendDefensiveReport();
        commManager.ProcessQueue();

        // Assert
        Assert.Equal(0, messageCount);
    }

    [Fact]
    public void Aircraft_EngagementRadioMessages_UseFriendlySupportAffiliation()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendFox3Report("TRACK-1234", WeaponType.Aim120);
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(SpeakerAffiliation.FriendlySupport, receivedMessage.SpeakerAffiliation);
    }

    [Fact]
    public void Aircraft_EngagementRadioMessages_UseCommandNetChannel()
    {
        // Arrange
        var fighter = Aircraft.CreateFromType("F-16", Affiliation.Friendly);
        fighter.CallSign = "VIPER-1";
        var commManager = new CommManager();
        fighter.CommManager = commManager;

        RadioMessage? receivedMessage = null;
        commManager.MessageReceived += msg => receivedMessage = msg;

        // Act
        fighter.SendSplashReport("TRACK-1234");
        commManager.ProcessQueue();

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(RadioChannel.CommandNet, receivedMessage.Channel);
    }
}
