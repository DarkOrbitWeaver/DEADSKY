using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Events;

/// <summary>
/// Detects game events and triggers AI messages through the AIMessageQueue.
/// Monitors simulation state and raises AIGameEvents for significant changes.
/// </summary>
public sealed class GameEventDetector
{
    private readonly AIMessageQueue _messageQueue;
    private readonly AIMessageCooldownManager _cooldownManager;
    
    // Threat group detection state
    private readonly List<DateTime> _recentContactTimes = new();
    private const double ThreatGroupWindowSeconds = 30.0;
    private const int ThreatGroupThreshold = 2;
    private DateTime _lastThreatGroupMessageTime = DateTime.MinValue;
    private const double ThreatGroupMessageCooldownSeconds = 60.0;
    
    // Kill streak tracking
    private readonly List<DateTime> _recentKillTimes = new();
    private const double KillStreakWindowSeconds = 120.0;
    private const int KillStreakThreshold = 3;
    private DateTime _lastKillStreakMessageTime = DateTime.MinValue;
    private const double KillStreakMessageCooldownSeconds = 120.0;
    
    // Coordinated attack tracking
    private DateTime _lastCoordinatedAttackMessageTime = DateTime.MinValue;
    private const double CoordinatedAttackMessageCooldownSeconds = 90.0;
    
    // Aircraft type tracking
    private readonly HashSet<string> _detectedAircraftTypes = new();
    
    // Previous state tracking for change detection
    private string? _previousThreatLevel;
    private string? _previousMissionPhase;
    private DateTime _lastPhaseChangeTime = DateTime.MinValue;
    private const double MinPhaseChangeCooldownSeconds = 30.0;
    private readonly Dictionary<string, TrackClassification> _previousClassifications = new();
    private readonly Dictionary<string, string> _previousBehaviors = new();
    
    public GameEventDetector(AIMessageQueue messageQueue, AIMessageCooldownManager cooldownManager)
    {
        _messageQueue = messageQueue;
        _cooldownManager = cooldownManager;
    }
    
    /// <summary>
    /// Resets all tracking state (call when starting a new mission).
    /// </summary>
    public void Reset()
    {
        _recentContactTimes.Clear();
        _recentKillTimes.Clear();
        _detectedAircraftTypes.Clear();
        _previousThreatLevel = null;
        _previousMissionPhase = null;
        _previousClassifications.Clear();
        _previousBehaviors.Clear();
        _lastThreatGroupMessageTime = DateTime.MinValue;
        _lastKillStreakMessageTime = DateTime.MinValue;
        _lastCoordinatedAttackMessageTime = DateTime.MinValue;
    }
    
    /// <summary>
    /// Handles new contact detection events (9.1: Threat group detection).
    /// </summary>
    public void OnNewContact(NewContactEvent evt)
    {
        var now = DateTime.UtcNow;
        _recentContactTimes.Add(now);
        
        // Remove contacts outside the time window
        _recentContactTimes.RemoveAll(time => (now - time).TotalSeconds > ThreatGroupWindowSeconds);
        
        // Check if we've detected a threat group and cooldown has expired
        if (_recentContactTimes.Count >= ThreatGroupThreshold &&
            (now - _lastThreatGroupMessageTime).TotalSeconds >= ThreatGroupMessageCooldownSeconds)
        {
            var gameEvent = new AIGameEvent
            {
                EventType = GameEventType.ThreatGroupDetected,
                Timestamp = now,
                EventData = new Dictionary<string, object>
                {
                    ["ContactCount"] = _recentContactTimes.Count,
                    ["WindowSeconds"] = ThreatGroupWindowSeconds
                },
                Priority = EventPriority.Routine
            };
            
            _messageQueue.EnqueueMessage(gameEvent, "AlliedHQ", 
                $"Multiple contacts detected - {_recentContactTimes.Count} threats within {ThreatGroupWindowSeconds} seconds");
            
            _lastThreatGroupMessageTime = now;
        }
    }
    
    /// <summary>
    /// Handles engagement result events (9.6: High kill streak).
    /// </summary>
    public void OnEngagementResult(EngagementResultEvent evt)
    {
        if (!evt.WasKill)
            return;
            
        var now = DateTime.UtcNow;
        _recentKillTimes.Add(now);
        
        // Remove kills outside the time window
        _recentKillTimes.RemoveAll(time => (now - time).TotalSeconds > KillStreakWindowSeconds);
        
        // Check for kill streak and cooldown
        if (_recentKillTimes.Count >= KillStreakThreshold &&
            (now - _lastKillStreakMessageTime).TotalSeconds >= KillStreakMessageCooldownSeconds)
        {
            var gameEvent = new AIGameEvent
            {
                EventType = GameEventType.HighKillStreak,
                Timestamp = now,
                EventData = new Dictionary<string, object>
                {
                    ["KillCount"] = _recentKillTimes.Count,
                    ["WindowSeconds"] = KillStreakWindowSeconds
                },
                Priority = EventPriority.Routine
            };
            
            _messageQueue.EnqueueMessage(gameEvent, "AlliedHQ", 
                $"Excellent work! {_recentKillTimes.Count} confirmed kills in the last {KillStreakWindowSeconds / 60:F0} minutes");
            
            _lastKillStreakMessageTime = now;
        }
    }
    
    /// <summary>
    /// Handles battery damage events (9.3: Battery damage).
    /// </summary>
    public void OnBatteryDamaged(BatteryDamagedEvent evt)
    {
        var now = DateTime.UtcNow;
        var gameEvent = new AIGameEvent
        {
            EventType = GameEventType.BatteryDamaged,
            Timestamp = now,
            EventData = new Dictionary<string, object>
            {
                ["Component"] = evt.ComponentDamaged,
                ["Severity"] = evt.SeverityPct
            },
            Priority = EventPriority.Urgent  // Battery damage is urgent per Requirement 6.4
        };
        
        _messageQueue.EnqueueMessage(gameEvent, "AlliedHQ", 
            $"Battery damage reported - {evt.ComponentDamaged} at {evt.SeverityPct:F0}% severity");
    }
    
    /// <summary>
    /// Handles alert level changes (9.2: Threat level escalation).
    /// </summary>
    public void OnAlertLevelChanged(AlertLevelChangedEvent evt)
    {
        // Check for escalation (medium→high, high→critical)
        bool isEscalation = IsEscalation(evt.OldLevel, evt.NewLevel);
        
        if (isEscalation)
        {
            var now = DateTime.UtcNow;
            var gameEvent = new AIGameEvent
            {
                EventType = GameEventType.ThreatLevelEscalated,
                Timestamp = now,
                EventData = new Dictionary<string, object>
                {
                    ["OldLevel"] = evt.OldLevel,
                    ["NewLevel"] = evt.NewLevel
                },
                Priority = EventPriority.Routine
            };
            
            _messageQueue.EnqueueMessage(gameEvent, "AlliedHQ", 
                $"Threat level escalated from {evt.OldLevel} to {evt.NewLevel}");
        }
        
        _previousThreatLevel = evt.NewLevel;
    }
    
    /// <summary>
    /// Handles reinforcement arrival events (9.4: Support availability).
    /// </summary>
    public void OnReinforcementArrived(ReinforcementArrivedEvent evt)
    {
        var now = DateTime.UtcNow;
        var gameEvent = new AIGameEvent
        {
            EventType = GameEventType.SupportAvailable,
            Timestamp = now,
            EventData = new Dictionary<string, object>
            {
                ["UnitType"] = evt.UnitType,
                ["Callsign"] = evt.Callsign
            },
            Priority = EventPriority.Routine
        };
        
        _messageQueue.EnqueueMessage(gameEvent, "AlliedHQ", 
            $"Friendly support available - {evt.UnitType} {evt.Callsign} on station");
    }
    
    /// <summary>
    /// Analyzes simulation snapshot for various event conditions.
    /// Call this periodically (e.g., every simulation tick).
    /// </summary>
    public void AnalyzeSnapshot(SimulationSnapshot snapshot)
    {
        if (snapshot.Battery == null)
            return;
            
        // 9.5: Mission phase change detection
        DetectMissionPhaseChange(snapshot);
        
        // 9.7: Coordinated attack detection
        DetectCoordinatedAttack(snapshot);
        
        // 9.8: Threat classification changes
        DetectClassificationChanges(snapshot);
        
        // 9.9: New aircraft type detection
        DetectNewAircraftTypes(snapshot);
        
        // 9.10: Enemy behavior changes
        DetectBehaviorChanges(snapshot);
    }
    
    /// <summary>
    /// Detects mission phase changes (patrol → combat → recovery).
    /// NOTE: These messages are disabled as they provide no actionable information to the player.
    /// The phase is still tracked internally for AI context.
    /// </summary>
    private void DetectMissionPhaseChange(SimulationSnapshot snapshot)
    {
        // Determine current mission phase based on game state
        string currentPhase;
        if (snapshot.ActiveMissiles.Count > 0)
        {
            currentPhase = "ENGAGEMENT";
        }
        else if (snapshot.HostileTracks.Count > 0)
        {
            currentPhase = "COMBAT";
        }
        else if (snapshot.Battery?.DamagePct > 0)
        {
            currentPhase = "RECOVERY";
        }
        else
        {
            currentPhase = "PATROL";
        }
        
        // Update phase tracking silently (no messages sent)
        // This is used internally by AI for context but doesn't clutter player comms
        if (_previousMissionPhase != currentPhase)
        {
            _previousMissionPhase = currentPhase;
            _lastPhaseChangeTime = DateTime.UtcNow;
        }
        else if (_previousMissionPhase == null)
        {
            // First time initialization
            _previousMissionPhase = currentPhase;
            _lastPhaseChangeTime = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Detects coordinated attacks (3+ threats from different vectors).
    /// </summary>
    private void DetectCoordinatedAttack(SimulationSnapshot snapshot)
    {
        var hostileTracks = snapshot.HostileTracks;
        if (hostileTracks.Count < 3)
            return;
            
        var battery = snapshot.Battery;
        if (battery == null)
            return;
            
        var now = DateTime.UtcNow;
        
        // Check cooldown
        if ((now - _lastCoordinatedAttackMessageTime).TotalSeconds < CoordinatedAttackMessageCooldownSeconds)
            return;
            
        // Calculate bearing sectors for each threat
        var sectors = new HashSet<int>();
        foreach (var track in hostileTracks)
        {
            var dx = track.Position.X - battery.Position.X;
            var dy = track.Position.Y - battery.Position.Y;
            var bearing = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (bearing < 0) bearing += 360;
            
            // Divide into 8 sectors (45 degrees each)
            int sector = (int)(bearing / 45.0);
            sectors.Add(sector);
        }
        
        // If threats are coming from 3+ different sectors, it's coordinated
        if (sectors.Count >= 3)
        {
            var gameEvent = new AIGameEvent
            {
                EventType = GameEventType.CoordinatedAttackDetected,
                Timestamp = now,
                EventData = new Dictionary<string, object>
                {
                    ["ThreatCount"] = hostileTracks.Count,
                    ["VectorCount"] = sectors.Count
                },
                Priority = EventPriority.Routine
            };
            
            _messageQueue.EnqueueMessage(gameEvent, "Intel", 
                $"Coordinated attack pattern detected - {hostileTracks.Count} threats from {sectors.Count} vectors");
            
            _lastCoordinatedAttackMessageTime = now;
        }
    }
    
    /// <summary>
    /// Detects threat classification changes (unknown→hostile).
    /// </summary>
    private void DetectClassificationChanges(SimulationSnapshot snapshot)
    {
        foreach (var track in snapshot.AllTracks)
        {
            if (_previousClassifications.TryGetValue(track.TrackId, out var oldClassification))
            {
                if (oldClassification == TrackClassification.Unknown && 
                    track.Classification == TrackClassification.Hostile)
                {
                    var now = DateTime.UtcNow;
                    var gameEvent = new AIGameEvent
                    {
                        EventType = GameEventType.ThreatClassificationChanged,
                        Timestamp = now,
                        EventData = new Dictionary<string, object>
                        {
                            ["TrackId"] = track.TrackId,
                            ["OldClassification"] = oldClassification.ToString(),
                            ["NewClassification"] = track.Classification.ToString()
                        },
                        Priority = EventPriority.Routine
                    };
                    
                    _messageQueue.EnqueueMessage(gameEvent, "Intel", 
                        $"Track {track.TrackId} reclassified from {oldClassification} to {track.Classification}");
                }
            }
            
            _previousClassifications[track.TrackId] = track.Classification;
        }
    }
    
    /// <summary>
    /// Detects new aircraft types (first time in mission).
    /// </summary>
    private void DetectNewAircraftTypes(SimulationSnapshot snapshot)
    {
        foreach (var aircraft in snapshot.HostileAircraft)
        {
            if (!string.IsNullOrEmpty(aircraft.Designation) && _detectedAircraftTypes.Add(aircraft.Designation))
            {
                var now = DateTime.UtcNow;
                var gameEvent = new AIGameEvent
                {
                    EventType = GameEventType.NewAircraftTypeDetected,
                    Timestamp = now,
                    EventData = new Dictionary<string, object>
                    {
                        ["AircraftType"] = aircraft.Designation,
                        ["Role"] = aircraft.Role.ToString()
                    },
                    Priority = EventPriority.Routine
                };
                
                _messageQueue.EnqueueMessage(gameEvent, "Intel", 
                    $"New aircraft type detected: {aircraft.Designation} ({aircraft.Role})");
            }
        }
    }
    
    /// <summary>
    /// Detects enemy behavior changes.
    /// </summary>
    private void DetectBehaviorChanges(SimulationSnapshot snapshot)
    {
        foreach (var aircraft in snapshot.HostileAircraft)
        {
            var currentBehavior = aircraft.CurrentBehavior.ToString();
            
            if (_previousBehaviors.TryGetValue(aircraft.Id, out var oldBehavior))
            {
                if (oldBehavior != currentBehavior && IsSignificantBehaviorChange(oldBehavior, currentBehavior))
                {
                    var now = DateTime.UtcNow;
                    var gameEvent = new AIGameEvent
                    {
                        EventType = GameEventType.EnemyBehaviorChanged,
                        Timestamp = now,
                        EventData = new Dictionary<string, object>
                        {
                            ["AircraftId"] = aircraft.Id,
                            ["OldBehavior"] = oldBehavior,
                            ["NewBehavior"] = currentBehavior
                        },
                        Priority = EventPriority.Routine
                    };
                    
                    _messageQueue.EnqueueMessage(gameEvent, "Intel", 
                        $"Enemy behavior change detected: {oldBehavior} → {currentBehavior}");
                }
            }
            
            _previousBehaviors[aircraft.Id] = currentBehavior;
        }
    }
    
    /// <summary>
    /// Determines if an alert level change is an escalation.
    /// </summary>
    private static bool IsEscalation(string oldLevel, string newLevel)
    {
        var levels = new[] { "Green", "Yellow", "Orange", "Red", "Black" };
        int oldIndex = Array.IndexOf(levels, oldLevel);
        int newIndex = Array.IndexOf(levels, newLevel);
        
        if (oldIndex < 0 || newIndex < 0)
            return false;
            
        return newIndex > oldIndex;
    }
    
    /// <summary>
    /// Determines if a behavior change is significant enough to report.
    /// </summary>
    private static bool IsSignificantBehaviorChange(string oldBehavior, string newBehavior)
    {
        // Filter out minor state transitions
        var significantChanges = new[]
        {
            ("Attack", "Retreat"),
            ("Patrol", "Attack"),
            ("Retreat", "Attack"),
            ("Ingress", "Egress")
        };
        
        foreach (var (from, to) in significantChanges)
        {
            if (oldBehavior.Contains(from, StringComparison.OrdinalIgnoreCase) && 
                newBehavior.Contains(to, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        return false;
    }
}
