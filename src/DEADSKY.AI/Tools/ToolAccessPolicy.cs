namespace DEADSKY.AI.Tools;

public enum AgentKnowledgeRole
{
    EnemyCommander,
    AlliedHQ,
    Intelligence,
    Crew
}

public sealed record KnowledgeEnvelope(
    AgentKnowledgeRole Role,
    bool HasFusedCommandPicture,
    bool HasBatteryTruth,
    bool HasSupportTruth,
    bool HasIncidentTruth,
    string Summary);

public static class ToolAccessPolicy
{
    public static IReadOnlyList<string> ResolveToolNames(AgentKnowledgeRole role) => role switch
    {
        AgentKnowledgeRole.EnemyCommander =>
        [
            "get_enemy_operational_brief",
            "get_radar_contacts",
            "get_contact_details",
            "get_threat_assessment",
            "send_radio_message",
            "broadcast_open_frequency",
            "set_group_tactic",
            "spawn_aircraft",
            "request_reinforcement",
            "log_event"
        ],
        AgentKnowledgeRole.AlliedHQ =>
        [
            "get_shared_operational_picture",
            "get_recent_incidents",
            "get_support_status",
            "get_battery_status",
            "get_threat_assessment",
            "get_engagement_history",
            "request_support_action",
            "cancel_support_action",
            "update_roe",
            "set_alert_level",
            "task_cap_intercept",
            "request_aircraft_launch",
            "get_airbase_status",
            // Phase 4: Battery coordination
            "task_battery_engage",
            "task_battery_hold_fire",
            "get_battery_network_status",
            "log_event"
        ],
        AgentKnowledgeRole.Intelligence =>
        [
            "get_radar_contacts",
            "get_contact_details",
            "get_threat_assessment",
            "get_shared_operational_picture",
            "get_recent_incidents",
            "get_engagement_history"
        ],
        _ =>
        [
            "get_battery_status",
            "get_radar_contacts",
            "get_contact_details",
            "get_recent_incidents"
        ]
    };

    public static KnowledgeEnvelope BuildEnvelope(AgentKnowledgeRole role) => role switch
    {
        AgentKnowledgeRole.EnemyCommander => new KnowledgeEnvelope(
            role,
            HasFusedCommandPicture: false,
            HasBatteryTruth: false,
            HasSupportTruth: false,
            HasIncidentTruth: false,
            "Enemy commander works from own-force status, observed radar pressure, losses, and open or inferred battlefield clues."),
        AgentKnowledgeRole.AlliedHQ => new KnowledgeEnvelope(
            role,
            HasFusedCommandPicture: true,
            HasBatteryTruth: true,
            HasSupportTruth: true,
            HasIncidentTruth: true,
            "Allied HQ receives the fused sector picture and can issue command-level support and ROE changes."),
        AgentKnowledgeRole.Intelligence => new KnowledgeEnvelope(
            role,
            HasFusedCommandPicture: true,
            HasBatteryTruth: false,
            HasSupportTruth: false,
            HasIncidentTruth: true,
            "Intel sees the surveillance picture and recent outcomes, but does not command the force."),
        _ => new KnowledgeEnvelope(
            role,
            HasFusedCommandPicture: false,
            HasBatteryTruth: true,
            HasSupportTruth: false,
            HasIncidentTruth: true,
            "Crew roles speak from the local battery picture, current control state, and recent local incidents.")
    };
}
