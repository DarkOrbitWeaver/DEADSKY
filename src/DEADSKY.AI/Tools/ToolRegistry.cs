using System.Text.Json;
using DEADSKY.AI.Client;
using DEADSKY.Core.Campaign;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.EnemyAI;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;
using DEADSKY.Core.Weapons;

namespace DEADSKY.AI.Tools;

/// <summary>
/// All game tools the AI can call. Each method is bound to a ToolDefinition
/// and executed when the model returns a tool_call with that name.
/// </summary>
public class ToolRegistry
{
    private readonly SimulationEngine _sim;
    private readonly GroupTacticManager _tactics;
    private readonly FriendlySupportDirector? _friendlySupport;
    private readonly Func<ScenarioDefinition?>? _scenarioAccessor;
    private readonly Dictionary<string, Func<ToolCall, Task<string>>> _handlers;
    private readonly Dictionary<string, ToolDefinition> _definitions;

    public SimulationEngine Simulation => _sim;
    public IReadOnlyList<ToolDefinition> AllTools =>
        _definitions.Values.ToList();
    public IReadOnlyList<ToolDefinition> EnemyCommanderTools => GetToolsByName(
    [
        "get_radar_contacts",
        "get_contact_details",
        "get_threat_assessment",
        "get_shared_operational_picture",
        "get_recent_incidents",
        "get_support_status",
        "send_radio_message",
        "broadcast_open_frequency",
        "set_group_tactic",
        "spawn_aircraft",
        "request_reinforcement",
        "request_support_action",
        "cancel_support_action",
        "log_event"
    ]);

    public ToolRegistry(
        SimulationEngine sim,
        GroupTacticManager tactics,
        FriendlySupportDirector? friendlySupport = null,
        Func<ScenarioDefinition?>? scenarioAccessor = null)
    {
        _sim = sim;
        _tactics = tactics;
        _friendlySupport = friendlySupport;
        _scenarioAccessor = scenarioAccessor;
        _handlers = new();
        _definitions = new();
        RegisterAll();
    }

    // ── Tool execution ────────────────────────────────────────────────

    public async Task<string> ExecuteAsync(ToolCall call)
    {
        if (_handlers.TryGetValue(call.Name, out var handler))
        {
            try { return await handler(call); }
            catch (Exception ex) { return $"{{\"error\":\"{ex.Message}\"}}"; }
        }
        return $"{{\"error\":\"Unknown tool: {call.Name}\"}}";
    }

    public IReadOnlyList<ToolDefinition> GetToolsByName(IEnumerable<string> names)
    {
        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => _definitions.ContainsKey(name))
            .Select(name => _definitions[name])
            .ToList();
    }

    // ── Registration ──────────────────────────────────────────────────

    private void RegisterAll()
    {
        // INTELLIGENCE / AWARENESS
        Register("get_radar_contacts",
            "Get all current radar contacts with position, heading, speed, altitude, classification.",
            new[] { ("filter", "optional: hostile|friendly|unknown|all"), ("max_range_nm", "optional: filter by range") },
            new string[0],
            GetRadarContacts);

        Register("get_contact_details",
            "Get detailed information about a specific tracked contact.",
            new[] { ("track_id", "required: track ID e.g. TRK-0023") },
            new[] { "track_id" },
            GetContactDetails);

        Register("get_battery_status",
            "Full battery status: launcher states, missile counts, readiness, damage.",
            Array.Empty<(string, string)>(), Array.Empty<string>(),
            GetBatteryStatus);

        Register("get_threat_assessment",
            "Ranked threat assessment of all current contacts.",
            Array.Empty<(string, string)>(), Array.Empty<string>(),
            GetThreatAssessment);

        Register("get_shared_operational_picture",
            "Get the fused operator picture used by UI and AI: threats, support, and recent consequences.",
            Array.Empty<(string, string)>(), Array.Empty<string>(),
            GetSharedOperationalPicture);

        Register("get_recent_incidents",
            "Recent engagement incidents, warnings, and consequence records.",
            new[] { ("last_n", "optional: number of incidents to return") },
            Array.Empty<string>(),
            GetRecentIncidents);

        Register("get_weather_conditions",
            "Current weather: visibility, cloud ceiling, precipitation, wind.",
            Array.Empty<(string, string)>(), Array.Empty<string>(),
            GetWeather);

        Register("get_allied_positions",
            "Get positions and status of all allied forces.",
            new[] { ("type_filter", "optional: sam_battery|fighter|awacs|ground_unit") },
            Array.Empty<string>(),
            GetAlliedPositions);

        Register("get_support_status",
            "Get status of friendly support actors and current availability.",
            Array.Empty<(string, string)>(),
            Array.Empty<string>(),
            GetSupportStatus);

        // COMMUNICATIONS
        Register("send_radio_message",
            "Send a radio message on a specific channel as a character in the game.",
            new[]
            {
                ("channel", "required: command|battery|air_defense|intel|guard|open"),
                ("sender_callsign", "required: who is speaking"),
                ("message", "required: the message content (use brevity codes naturally)"),
                ("priority", "required: flash|immediate|priority|routine"),
                ("recipient_callsign", "optional: directed message to specific unit")
            },
            new[] { "channel", "sender_callsign", "message", "priority" },
            SendRadioMessage);

        Register("broadcast_alert",
            "Broadcast an alert to all channels simultaneously.",
            new[]
            {
                ("alert_type", "required: air_raid|missile_incoming|cease_fire|all_clear|ecm_detected"),
                ("message", "required: alert details")
            },
            new[] { "alert_type", "message" },
            BroadcastAlert);

        Register("broadcast_open_frequency",
            "Send a message on open frequency as a hostile, civilian, or support actor.",
            new[]
            {
                ("sender_callsign", "required: speaker name or callsign"),
                ("message", "required: transmission content"),
                ("intent", "required: warning|surrender|taunt|distress|panic"),
                ("priority", "optional: flash|immediate|priority|routine")
            },
            new[] { "sender_callsign", "message", "intent" },
            BroadcastOpenFrequency);

        // ENEMY BEHAVIOR CONTROL
        Register("set_aircraft_behavior",
            "Set behavior/intent for an enemy aircraft or group.",
            new[]
            {
                ("track_id_or_group_id", "required: entity id, track id, or group id"),
                ("behavior", "required: ingress_attack|egress_retreat|orbit_patrol|evasive_maneuver|ecm_standoff|terrain_following|pop_up_attack|feint|sead"),
                ("aggressiveness", "optional: 0.0-1.0")
            },
            new[] { "track_id_or_group_id", "behavior" },
            SetAircraftBehavior);

        Register("change_flight_path",
            "Change an aircraft's target heading, altitude, and speed.",
            new[]
            {
                ("entity_id", "required: aircraft entity id"),
                ("heading_deg", "optional: new heading"),
                ("altitude_ft", "optional: new altitude"),
                ("speed_kts", "optional: new speed")
            },
            new[] { "entity_id" },
            ChangeFlightPath);

        Register("activate_ecm",
            "Activate electronic countermeasures on an aircraft.",
            new[]
            {
                ("entity_id", "required: aircraft entity id"),
                ("activate", "required: true|false")
            },
            new[] { "entity_id", "activate" },
            ActivateECM);

        Register("set_group_tactic",
            "Set coordinated tactic for an aircraft group.",
            new[]
            {
                ("group_id", "required: group identifier"),
                ("tactic", "required: straight_ingress|pincer_attack|feint_and_strike|ecm_escort|saturation|pop_up|terrain_masking|drone_decoy|sead|time_on_target")
            },
            new[] { "group_id", "tactic" },
            SetGroupTactic);

        Register("spawn_aircraft",
            "Spawn new enemy aircraft into the scenario.",
            new[]
            {
                ("type", "required: fighter|bomber|cruise_missile|drone|ecm_aircraft"),
                ("designation", "required: e.g. SU-24, F-16, MQ-9"),
                ("bearing_from_battery", "required: initial bearing degrees"),
                ("range_nm", "required: initial range"),
                ("altitude_ft", "required: initial altitude"),
                ("heading_deg", "required: initial heading"),
                ("speed_kts", "required: initial speed"),
                ("count", "optional: number of aircraft"),
                ("group_id", "optional: assign to group")
            },
            new[] { "designation", "bearing_from_battery", "range_nm", "altitude_ft", "heading_deg", "speed_kts" },
            SpawnAircraft);

        // GAME STATE
        Register("set_alert_level",
            "Change the battery alert level.",
            new[] { ("level", "required: green|yellow|orange|red|black") },
            new[] { "level" },
            SetAlertLevel);

        Register("update_roe",
            "Change current rules of engagement.",
            new[]
            {
                ("roe", "required: weapons_free|weapons_tight|weapons_hold"),
                ("reason", "required: justification for change"),
                ("authority", "required: who is authorizing this")
            },
            new[] { "roe", "reason", "authority" },
            UpdateROE);

        Register("log_event",
            "Log a significant event to the mission record.",
            new[]
            {
                ("event_type", "required: engagement|detection|communication|alert|damage|other"),
                ("description", "required: what happened"),
                ("significance", "required: critical|major|minor|info")
            },
            new[] { "event_type", "description", "significance" },
            LogEvent);

        Register("report_engagement_result",
            "Report outcome of a missile engagement.",
            new[]
            {
                ("track_id", "required: target track ID"),
                ("result", "required: kill_confirmed|probable_kill|miss|target_damaged"),
                ("details", "optional: additional context")
            },
            new[] { "track_id", "result" },
            ReportEngagementResult);

        Register("request_reinforcement",
            "Request allied reinforcement assets.",
            new[]
            {
                ("type", "required: fighter_cap|additional_sam|awacs|ground_resupply"),
                ("urgency", "required: flash|immediate|priority|routine"),
                ("justification", "required: why reinforcement is needed")
            },
            new[] { "type", "urgency", "justification" },
            RequestReinforcement);

        Register("request_support_action",
            "Request a friendly support actor action such as picture relay, CAP diversion, or relay recovery.",
            new[]
            {
                ("support_type", "required: picture|declare|cap|jam|relay|battery|awacs|sar"),
                ("requestor", "required: who is requesting"),
                ("details", "required: support reason or cue")
            },
            new[] { "support_type", "requestor", "details" },
            RequestSupportAction);

        Register("cancel_support_action",
            "Cancel a previously tasked friendly support package.",
            new[] { ("package_id", "required: support package id") },
            new[] { "package_id" },
            CancelSupportAction);

        Register("get_engagement_history",
            "History of all engagements this mission.",
            new[] { ("last_n", "optional: last N engagements") },
            Array.Empty<string>(),
            GetEngagementHistory);
    }

    private void Register(
        string name, string description,
        (string name, string desc)[] parameters,
        string[] required,
        Func<ToolCall, Task<string>> handler)
    {
        var paramDict = parameters.ToDictionary(
            p => p.name,
            p => (object)new { type = "string", description = p.desc });

        _definitions[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = paramDict,
            RequiredParameters = required.ToList()
        };
        _handlers[name] = handler;
    }

    // ── Tool implementations ──────────────────────────────────────────

    private Task<string> GetRadarContacts(ToolCall call)
    {
        var filter = call.GetString("filter", "all");
        var maxRange = call.GetDouble("max_range_nm", 999);
        var snapshot = _sim.LatestSnapshot;

        var tracks = snapshot.AllTracks
            .Where(t =>
            {
                if (t.RangeNm > maxRange) return false;
                return filter switch
                {
                    "hostile" => t.Classification is TrackClassification.Hostile or TrackClassification.AssumedHostile,
                    "friendly" => t.Classification == TrackClassification.Friendly,
                    "unknown" => t.Classification == TrackClassification.Unknown,
                    _ => true
                };
            })
            .Select(t => new
            {
                track_id = t.TrackId,
                designation = t.TrackDesignation,
                classification = t.Classification.ToString(),
                bearing_deg = t.BearingDeg,
                range_nm = Math.Round(t.RangeNm, 1),
                altitude_ft = (int)t.AltitudeFt,
                speed_kts = (int)t.SpeedKts,
                heading_deg = (int)t.HeadingDeg,
                aspect = t.AspectString,
                track_quality = t.Quality.ToString(),
                threat_level = Math.Round(t.ThreatLevel, 2),
                time_to_threat_sec = t.TimeToThreatSec > 9999 ? "N/A" : $"{t.TimeToThreatSec:F0}s",
                is_being_engaged = t.IsBeingEngaged
            })
            .ToList();

        return Task.FromResult(JsonSerializer.Serialize(new { contacts_count = tracks.Count, contacts = tracks }));
    }

    private Task<string> GetContactDetails(ToolCall call)
    {
        var trackId = call.GetString("track_id");
        var track = _sim.Radar.TrackManager.GetById(trackId);
        if (track == null) return Task.FromResult("{\"error\":\"Track not found\"}");

        var entity = track.EntityId != null ? _sim.Entities.Get(track.EntityId) as Aircraft : null;

        var detail = new
        {
            track_id = track.TrackId,
            designation = track.TrackDesignation,
            classification = track.Classification.ToString(),
            bearing = track.BearingDeg,
            range_nm = track.RangeNm,
            altitude_ft = track.AltitudeFt,
            speed_kts = track.SpeedKts,
            heading_deg = track.HeadingDeg,
            aspect = track.AspectString,
            quality = track.Quality.ToString(),
            iff_response = track.IFFResponse,
            time_since_last_detection_sec = track.TimeSinceLastDetectionSec,
            position_uncertainty_m = track.PositionUncertaintyM,
            is_designated = track.IsDesignated,
            is_being_engaged = track.IsBeingEngaged,
            ecm_active = entity?.ECMActive ?? false,
            behavior = entity?.CurrentBehavior.ToString() ?? "UNKNOWN",
            fuel_remaining = entity?.FuelRemainingKg ?? 0
        };

        return Task.FromResult(JsonSerializer.Serialize(detail));
    }

    private Task<string> GetBatteryStatus(ToolCall call)
    {
        var battery = _sim.Entities.GetPlayerBattery();
        if (battery == null) return Task.FromResult("{\"error\":\"No battery\"}");

        var status = new
        {
            callsign = battery.Callsign,
            alert_level = battery.AlertLevel.ToString(),
            roe = battery.ROE.ToString(),
            radar_online = battery.RadarOnline,
            radar_mode = battery.RadarMode.ToString(),
            radar_range_nm = battery.RadarRangeNm,
            radar_health_pct = battery.RadarHealthPct,
            power_online = battery.PowerOnline,
            comms_online = battery.CommsOnline,
            launchers = battery.Launchers.Select(l => new
            {
                id = l.Id,
                state = l.State.ToString(),
                missile_type = l.MissileType,
                reload_progress = Math.Round(l.ReloadProgress, 2)
            }),
            ready_launchers = battery.ReadyLaunchers,
            reloading_launchers = battery.ReloadingLaunchers,
            reserve_missiles = battery.ReserveMissiles,
            current_weapon_id = battery.CurrentWeaponId,
            current_weapon = WeaponCatalog.Get(battery.CurrentWeaponId).DisplayName,
            missiles_fired = battery.MissilesFired,
            confirmed_kills = battery.ConfirmedKills,
            hit_rate_pct = Math.Round(battery.HitRate * 100, 1),
            designated_target = battery.DesignatedTargetId
        };

        return Task.FromResult(JsonSerializer.Serialize(status));
    }

    private Task<string> GetThreatAssessment(ToolCall call)
    {
        var tracks = _sim.Radar.TrackManager.GetHostileTracks()
            .OrderByDescending(t => t.ThreatLevel)
            .Take(10)
            .Select(t => new
            {
                track_id = t.TrackId,
                designation = t.TrackDesignation,
                threat_level = t.ThreatLevel,
                range_nm = Math.Round(t.RangeNm, 1),
                time_to_envelope = t.TimeToThreatSec > 9999 ? "NOT CLOSING" : $"{t.TimeToThreatSec:F0}s",
                aspect = t.AspectString
            });

        return Task.FromResult(JsonSerializer.Serialize(new { threat_summary = tracks }));
    }

    private Task<string> GetSharedOperationalPicture(ToolCall call)
    {
        var picture = OperationalPictureBuilder.Build(
            _sim.LatestSnapshot,
            _sim.Weapons.Incidents,
            _friendlySupport?.Packages);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            threat_summary = picture.ThreatSummary,
            support_summary = picture.SupportSummary,
            consequence_summary = picture.ConsequenceSummary,
            threats = picture.ThreatStates,
            friendlies = picture.FriendlyForces
        }));
    }

    private Task<string> GetRecentIncidents(ToolCall call)
    {
        int lastN = Math.Max(1, call.GetInt("last_n", 8));
        var incidents = _sim.Weapons.Incidents
            .TakeLast(lastN)
            .Select(incident => new
            {
                type = incident.IncidentType,
                summary = incident.Summary,
                severity = incident.Severity.ToString(),
                timestamp_utc = incident.TimestampUtc,
                track_id = incident.TrackId,
                entity_id = incident.EntityId,
                weapon_id = incident.WeaponId
            });
        return Task.FromResult(JsonSerializer.Serialize(new { incidents }));
    }

    private Task<string> GetWeather(ToolCall call)
    {
        var w = _sim.Weather;
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            visibility_nm = w.VisibilityNm,
            cloud_ceiling_ft = w.CloudCeilingFt,
            precipitation_mm_hr = w.PrecipitationMmHr,
            description = w.Description,
            wind_speed_kts = w.WindSpeedKts,
            wind_direction_deg = w.WindDirectionDeg
        }));
    }

    private Task<string> GetAlliedPositions(ToolCall call)
    {
        var friendly = _sim.Entities.GetByAffiliation(Affiliation.Friendly)
            .Where(e => e.Type != EntityType.SAMBattery || e.Affiliation == Affiliation.Friendly)
            .Select(e =>
            {
                var (brg, rng) = e.BearingRange;
                return new { id = e.Id, designation = e.Designation, bearing = brg, range_nm = rng, status = e.Status.ToString() };
            }).ToList();
        IEnumerable<object> support = _friendlySupport == null
            ? Array.Empty<object>()
            : _friendlySupport.Packages.Select(package => (object)new
            {
                id = package.Id,
                designation = package.Designation,
                callsign = package.UnitCallsign,
                status = package.Availability.ToString(),
                visible = package.IsVisibleInPicture
            });

        return Task.FromResult(JsonSerializer.Serialize(new { allied_units = friendly, support_units = support }));
    }

    private Task<string> GetSupportStatus(ToolCall call)
    {
        if (_friendlySupport == null)
            return Task.FromResult("{\"error\":\"support director unavailable\"}");

        var payload = _friendlySupport.Packages.Select(package => new
        {
            package_id = package.Id,
            type = package.Type.ToString(),
            display_name = package.DisplayName,
            callsign = package.UnitCallsign,
            availability = package.Availability.ToString(),
            delay_sec = Math.Round(package.DelayRemainingSec, 1),
            cooldown_sec = Math.Round(package.CooldownRemainingSec, 1),
            visible_in_picture = package.IsVisibleInPicture,
            summary = package.LastSummary
        });

        return Task.FromResult(JsonSerializer.Serialize(new { support_packages = payload }));
    }

    private Task<string> SendRadioMessage(ToolCall call)
    {
        var channelStr = call.GetString("channel", "command");
        var sender = call.GetString("sender_callsign");
        var message = call.GetString("message");
        var priorityStr = call.GetString("priority", "routine");
        var recipient = call.GetString("recipient_callsign", "");
        var messageTypeStr = call.GetString("message_type", "normal");

        var channel = channelStr.ToLower() switch
        {
            "battery" => RadioChannel.BatteryNet,
            "air_defense" => RadioChannel.AirDefenseNet,
            "intel" => RadioChannel.IntelNet,
            "guard" => RadioChannel.Guard,
            "open" => RadioChannel.OpenFreq,
            _ => RadioChannel.CommandNet
        };

        var priority = priorityStr.ToLower() switch
        {
            "flash" => MessagePriority.Flash,
            "immediate" => MessagePriority.Immediate,
            "priority" => MessagePriority.Priority,
            _ => MessagePriority.Routine
        };

        var messageType = messageTypeStr.ToLowerInvariant() switch
        {
            "alert" => MessageType.Alert,
            "status" => MessageType.StatusReport,
            "intel" => MessageType.IntelUpdate,
            "chatter" => MessageType.RadioChatter,
            _ => MessageType.Normal
        };

        var speaker = ResolveSpeaker(sender, channel);
        var msg = CommManager.CreateMessage(
            speaker,
            channel,
            message,
            priority,
            messageType,
            recipient: string.IsNullOrEmpty(recipient) ? null : recipient,
            canReply: channel is RadioChannel.CommandNet or RadioChannel.IntelNet or RadioChannel.OpenFreq,
            staticLevel: 0.1 + SimulationRandom.Instance.NextDouble() * 0.1);

        _sim.Comms.Queue(msg);
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            status = "queued",
            effective_channel = msg.Channel.ToString(),
            effective_type = msg.Type.ToString(),
            header = msg.DisplayHeader
        }));
    }

    private Task<string> BroadcastAlert(ToolCall call)
    {
        var alertType = call.GetString("alert_type");
        var message = call.GetString("message");
        var priority = alertType is "air_raid" or "missile_incoming" ? MessagePriority.Flash : MessagePriority.Immediate;

        foreach (RadioChannel ch in Enum.GetValues<RadioChannel>())
        {
            _sim.Comms.Queue(CommManager.CreateMessage(
                RadioRules.CreateSystemProfile("ALERT"),
                ch,
                $"[{alertType.Replace('_', ' ').ToUpper()}] {message}",
                priority,
                MessageType.Alert,
                canReply: false,
                staticLevel: 0.0));
        }
        return Task.FromResult("{\"status\":\"broadcast_sent_all_channels\"}");
    }

    private Task<string> BroadcastOpenFrequency(ToolCall call)
    {
        var sender = call.GetString("sender_callsign");
        var message = call.GetString("message");
        var intent = call.GetString("intent", "warning");
        var priority = call.GetString("priority", "priority").ToLowerInvariant() switch
        {
            "flash" => MessagePriority.Flash,
            "immediate" => MessagePriority.Immediate,
            "routine" => MessagePriority.Routine,
            _ => MessagePriority.Priority
        };

        var speaker = RadioRules.CreateEnemyProfile(sender, "HOSTILE OPEN", $"{intent.ToUpperInvariant()} TRAFFIC");
        var msg = CommManager.CreateOpenFrequencyMessage(speaker, message, priority, canReply: true);
        _sim.Comms.Queue(msg);
        return Task.FromResult(JsonSerializer.Serialize(new { status = "queued", header = msg.DisplayHeader, channel = msg.Channel.ToString() }));
    }

    private Task<string> SetAircraftBehavior(ToolCall call)
    {
        var id = call.GetString("track_id_or_group_id");
        var behaviorStr = call.GetString("behavior");
        var aggressiveness = call.GetDouble("aggressiveness", -1);

        var behavior = behaviorStr.ToLower() switch
        {
            "ingress_attack" => AircraftBehavior.IngressAttack,
            "egress_retreat" => AircraftBehavior.EgressRetreat,
            "evasive_maneuver" => AircraftBehavior.EvasiveManeuver,
            "terrain_following" => AircraftBehavior.TerrainFollowing,
            "pop_up_attack" => AircraftBehavior.PopUpAttack,
            "feint" => AircraftBehavior.Feint,
            "ecm_standoff" => AircraftBehavior.ECMStandoff,
            "orbit_patrol" => AircraftBehavior.OrbitPatrol,
            _ => AircraftBehavior.IngressAttack
        };

        // Try entity first, then track, then group
        var entity = _sim.Entities.Get(id) as Aircraft;
        if (entity == null)
        {
            var track = _sim.Radar.TrackManager.GetById(id);
            if (track?.EntityId != null)
                entity = _sim.Entities.Get(track.EntityId) as Aircraft;
        }

        int changed = 0;
        if (entity != null)
        {
            if (DoctrineRules.IsBehaviorAuthorized(entity, behavior))
            {
                entity.CurrentBehavior = behavior;
                if (aggressiveness >= 0) entity.AggressivenessLevel = aggressiveness;
                changed = 1;
            }
        }
        else
        {
            // Try as group
            var group = _tactics.GetGroup(id);
            if (group != null)
            {
                foreach (var aid in group.AircraftIds)
                {
                    var a = _sim.Entities.Get(aid) as Aircraft;
                    if (a != null && DoctrineRules.IsBehaviorAuthorized(a, behavior))
                    {
                        a.CurrentBehavior = behavior;
                        changed++;
                    }
                }
            }
        }

        return Task.FromResult($"{{\"changed\":{changed},\"behavior\":\"{behavior}\"}}");
    }

    private Task<string> ChangeFlightPath(ToolCall call)
    {
        var entityId = call.GetString("entity_id");
        var entity = _sim.Entities.Get(entityId) as Aircraft;
        if (entity == null) return Task.FromResult("{\"error\":\"Entity not found\"}");

        var heading = call.GetDouble("heading_deg", -1);
        var altFt = call.GetDouble("altitude_ft", -1);
        var speedKts = call.GetDouble("speed_kts", -1);

        if (heading >= 0) entity.RequestedHeadingDeg = heading;
        if (altFt >= 0) entity.RequestedAltitudeM = CoordinateSystem.FtToM(altFt);
        if (speedKts >= 0) entity.RequestedSpeedMps = CoordinateSystem.KtsToMps(speedKts);

        return Task.FromResult("{\"status\":\"updated\"}");
    }

    private Task<string> ActivateECM(ToolCall call)
    {
        var entityId = call.GetString("entity_id");
        var activate = call.GetString("activate", "true") == "true";
        var entity = _sim.Entities.Get(entityId) as Aircraft;
        if (entity == null) return Task.FromResult("{\"error\":\"Entity not found\"}");

        entity.ECMActive = activate && entity.HasECM;
        return Task.FromResult($"{{\"ecm_active\":{entity.ECMActive.ToString().ToLower()}}}");
    }

    private Task<string> SetGroupTactic(ToolCall call)
    {
        var groupId = call.GetString("group_id");
        var tacticStr = call.GetString("tactic");

        var tactic = tacticStr.ToLower() switch
        {
            "pincer_attack" => GroupTactic.PincerAttack,
            "feint_and_strike" => GroupTactic.FeintAndStrike,
            "ecm_escort" => GroupTactic.ECMEscortPackage,
            "saturation" => GroupTactic.Saturation,
            "pop_up" => GroupTactic.PopUpAttack,
            "terrain_masking" => GroupTactic.TerrainMasking,
            "sead" => GroupTactic.SEAD,
            "time_on_target" => GroupTactic.TimeOnTarget,
            _ => GroupTactic.StraightIngress
        };

        var group = _tactics.GetGroup(groupId);
        if (group == null)
            return Task.FromResult("{\"error\":\"Group not found\"}");

        var aircraft = group.AircraftIds
            .Select(id => _sim.Entities.Get(id) as Aircraft)
            .Where(a => a != null)
            .Cast<Aircraft>()
            .ToList();

        if (!DoctrineRules.IsGroupTacticAuthorized(tactic, aircraft))
            return Task.FromResult("{\"error\":\"Doctrine denied for this package composition\"}");

        _tactics.SetGroupTactic(groupId, tactic);
        return Task.FromResult($"{{\"group\":\"{groupId}\",\"tactic\":\"{tactic}\"}}");
    }

    private Task<string> SpawnAircraft(ToolCall call)
    {
        var designation = call.GetString("designation");
        var bearing = call.GetDouble("bearing_from_battery");
        var rangeNm = call.GetDouble("range_nm");
        var altFt = call.GetDouble("altitude_ft");
        var heading = call.GetDouble("heading_deg");
        var speedKts = call.GetDouble("speed_kts");
        var count = Math.Max(1, call.GetInt("count", 1));
        var groupId = call.GetString("group_id", $"WAVE-{DateTime.UtcNow.Ticks % 1000}");

        var spawned = new List<string>();
        for (int i = 0; i < count; i++)
        {
            double jitter = (SimulationRandom.Instance.NextDouble() - 0.5) * 10;
            var aircraft = _sim.Entities.SpawnAircraftAtBearingRange(
                designation, Affiliation.Hostile,
                bearing + jitter, rangeNm, altFt, heading, speedKts);
            aircraft.GroupId = groupId;
            spawned.Add(aircraft.Id);
        }

        // Auto-create group
        if (spawned.Count > 0)
            _tactics.CreateGroup(groupId, spawned, GroupTactic.StraightIngress);

        return Task.FromResult(JsonSerializer.Serialize(new { spawned_ids = spawned, group_id = groupId }));
    }

    private Task<string> SetAlertLevel(ToolCall call)
    {
        var level = call.GetString("level");
        var alertLevel = level.ToLower() switch
        {
            "green" => BatteryAlertLevel.Green,
            "orange" => BatteryAlertLevel.Orange,
            "red" => BatteryAlertLevel.Red,
            "black" => BatteryAlertLevel.Black,
            _ => BatteryAlertLevel.Yellow
        };
        _sim.SetAlertLevel(alertLevel);
        return Task.FromResult($"{{\"alert_level\":\"{alertLevel}\"}}");
    }

    private Task<string> UpdateROE(ToolCall call)
    {
        var roe = call.GetString("roe");
        var authority = call.GetString("authority", "ECHO ACTUAL");
        var reason = call.GetString("reason");

        var roeVal = roe.ToLower() switch
        {
            "weapons_free" => RulesOfEngagement.WeaponsFree,
            "weapons_hold" => RulesOfEngagement.WeaponsHold,
            _ => RulesOfEngagement.WeaponsTight
        };
        _sim.SetROE(roeVal, authority);

        _sim.Comms.Queue(CommManager.CreateAlliedHQMessage(
            $"ALPHA, {authority}. ROE change: {roe.Replace('_', ' ').ToUpper()}. Reason: {reason}. Acknowledge.",
            MessagePriority.Immediate));

        return Task.FromResult($"{{\"roe\":\"{roeVal}\",\"authority\":\"{authority}\"}}");
    }

    private Task<string> LogEvent(ToolCall call)
    {
        var desc = call.GetString("description");
        var sig = call.GetString("significance");
        Console.WriteLine($"[MISSION LOG] [{sig.ToUpper()}] {desc}");
        return Task.FromResult("{\"logged\":true}");
    }

    private Task<string> ReportEngagementResult(ToolCall call)
    {
        var trackId = call.GetString("track_id");
        var result = call.GetString("result");
        var details = call.GetString("details");

        _sim.Comms.Queue(CommManager.CreateAlliedHQMessage(
            $"ALPHA, ECHO. SPLASH confirmed {trackId}. {result.Replace('_', ' ').ToUpper()}. {details}",
            MessagePriority.Priority));

        return Task.FromResult("{\"acknowledged\":true}");
    }

    private Task<string> RequestReinforcement(ToolCall call)
    {
        var type = call.GetString("type");
        var urgency = call.GetString("urgency");
        var justification = call.GetString("justification");

        _sim.Comms.Queue(CommManager.CreateAlliedHQMessage(
            $"ALPHA, ECHO. Reinforcement request {type.Replace('_', ' ')} received. " +
            $"Urgency: {urgency.ToUpper()}. Evaluating availability. Stand by.",
            MessagePriority.Priority));

        return Task.FromResult("{\"request_received\":true}");
    }

    private Task<string> RequestSupportAction(ToolCall call)
    {
        if (_friendlySupport == null)
            return Task.FromResult("{\"error\":\"support director unavailable\"}");

        var supportType = call.GetString("support_type");
        var requestor = call.GetString("requestor");
        var details = call.GetString("details");
        var result = _friendlySupport.RequestSupport(ParseSupportType(supportType), requestor, details, _sim.GameTimeSec);
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            accepted = result.Accepted,
            summary = result.Summary,
            package_id = result.PackageId,
            eta_sec = result.EtaSec,
            visible_support = result.VisibleSupport
        }));
    }

    private Task<string> CancelSupportAction(ToolCall call)
    {
        if (_friendlySupport == null)
            return Task.FromResult("{\"error\":\"support director unavailable\"}");

        var packageId = call.GetString("package_id");
        string summary = _friendlySupport.CancelSupport(packageId);
        return Task.FromResult(JsonSerializer.Serialize(new { summary }));
    }

    private Task<string> GetEngagementHistory(ToolCall call)
    {
        int n = call.GetInt("last_n", 10);
        var history = _sim.Weapons.EngagementHistory
            .TakeLast(n)
            .Select(e => new
            {
                track_id = e.TrackId,
                result = e.Result.ToString(),
                launch_time = e.LaunchTime.ToString("HH:mm:ss"),
                notes = e.Notes
            });
        return Task.FromResult(JsonSerializer.Serialize(new { engagements = history }));
    }

    private static FriendlySupportType ParseSupportType(string supportType) => supportType.ToLowerInvariant() switch
    {
        "picture" => FriendlySupportType.PictureRelay,
        "declare" => FriendlySupportType.DeclarationCell,
        "cap" => FriendlySupportType.CombatAirPatrol,
        "jam" => FriendlySupportType.JammingSupport,
        "relay" => FriendlySupportType.RelayRecovery,
        "battery" => FriendlySupportType.NearbyBattery,
        "awacs" => FriendlySupportType.Awacs,
        "sar" => FriendlySupportType.SearchAndRescue,
        _ => FriendlySupportType.PictureRelay
    };

    private static RadioSpeakerProfile ResolveSpeaker(string sender, RadioChannel channel)
    {
        string normalized = sender.ToUpperInvariant();
        if (normalized.Contains("ECHO"))
            return RadioRules.CreateAlliedHQProfile(normalized);
        if (normalized.Contains("INTEL") || normalized.Contains("SIGINT"))
            return RadioRules.CreateIntelProfile(normalized);
        if (normalized.Contains("VIPER") || normalized.Contains("SABLE") || normalized.Contains("LANTERN") || normalized.Contains("BRAVO"))
            return RadioRules.CreateFriendlySupportProfile(normalized, "SUPPORT", normalized, "NETWORK ACTOR");
        if (channel == RadioChannel.OpenFreq || normalized.Contains("RAVEN") || normalized.Contains("HOSTILE"))
            return RadioRules.CreateEnemyProfile(normalized, "HOSTILE NET", "ENEMY TRAFFIC");
        return RadioRules.CreateCrewProfile(normalized);
    }
}
