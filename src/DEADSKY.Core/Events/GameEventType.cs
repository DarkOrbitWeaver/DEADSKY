namespace DEADSKY.Core.Events;

/// <summary>
/// Defines the types of game events that can trigger AI communication.
/// </summary>
public enum GameEventType
{
    /// <summary>
    /// Triggered when 2+ contacts are detected within 30 seconds.
    /// </summary>
    ThreatGroupDetected,

    /// <summary>
    /// Triggered when threat level escalates from medium to high or high to critical.
    /// </summary>
    ThreatLevelEscalated,

    /// <summary>
    /// Triggered when player battery takes damage.
    /// </summary>
    BatteryDamaged,

    /// <summary>
    /// Triggered when friendly support becomes available (fighters, AWACS, jammers).
    /// </summary>
    SupportAvailable,

    /// <summary>
    /// Triggered when mission phase changes (patrol → combat → recovery).
    /// </summary>
    MissionPhaseChanged,

    /// <summary>
    /// Triggered when player achieves 3+ kills within 120 seconds.
    /// </summary>
    HighKillStreak,

    /// <summary>
    /// Triggered when 3+ threats from different vectors are detected.
    /// </summary>
    CoordinatedAttackDetected,

    /// <summary>
    /// Triggered when threat classification changes from unknown to hostile.
    /// </summary>
    ThreatClassificationChanged,

    /// <summary>
    /// Triggered when new aircraft type is detected for first time in mission.
    /// </summary>
    NewAircraftTypeDetected,

    /// <summary>
    /// Triggered when enemy behavior changes significantly (retreat, reinforcement, formation change).
    /// </summary>
    EnemyBehaviorChanged
}
