using DEADSKY.AI.Client;
using DEADSKY.AI.Tools;
using DEADSKY.Core.Campaign;
using DEADSKY.Core.Comms;
using DEADSKY.Core.EnemyAI;
using DEADSKY.Core.Logging;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.AI.Agents;

/// <summary>
/// Base class for all AI agents. Each agent has a system prompt persona,
/// conversation history, and access to specific tools.
/// </summary>
public abstract class AgentBase
{
    private const int ToolResultPreviewChars = 900;
    protected readonly AIModelClient _client;
    protected readonly ToolRegistry _tools;
    protected readonly List<ChatMessage> _history = new();
    protected const int MaxHistoryMessages = 10;

    public string AgentName { get; init; } = "";
    public DateTime LastRunTime { get; protected set; } = DateTime.MinValue;
    public bool IsRunning { get; protected set; }
    protected bool LastRunUsedTools { get; private set; }

    protected AgentBase(AIModelClient client, ToolRegistry tools)
    {
        _client = client;
        _tools = tools;
    }

    protected abstract string BuildSystemPrompt(SimulationSnapshot snapshot);

    /// <summary>
    /// Run the agent: build context, call AI, execute tool calls, return final text.
    /// Handles the full tool-use loop (up to 5 iterations to prevent infinite loops).
    /// </summary>
    protected async Task<string> RunAsync(
        SimulationSnapshot snapshot,
        string userContext,
        IReadOnlyList<ToolDefinition>? toolSubset = null,
        bool requireStructuredReply = false,
        double replyTemperature = 0.25,
        int replyMaxTokens = 96,
        bool finalReplyRequired = true,
        bool resetHistory = true,
        int planningMaxTokens = 96,
        int followUpMaxTokens = 80,
        CancellationToken ct = default)
    {
        if (IsRunning)
        {
            GameLogger.Warning("AI", $"{AgentName} already running, skipping request");
            return "";
        }
        
        IsRunning = true;
        LastRunTime = DateTime.UtcNow;
        GameLogger.Info("AI", $"{AgentName} starting: {userContext.Substring(0, Math.Min(100, userContext.Length))}...");

        try
        {
            var systemPrompt = BuildSystemPrompt(snapshot);
            var tools = (toolSubset ?? _tools.AllTools).ToList();

            if (resetHistory)
            {
                GameLogger.Debug("AI", $"{AgentName} resetting history");
                _history.Clear();
            }

            // Add user context to history
            _history.Add(ChatMessage.User(userContext));
            TrimHistory();

            string finalText = "";
            bool usedTools = false;
            
            GameLogger.Info("AI", $"{AgentName} calling AI model (maxTokens={planningMaxTokens})");
            var response = await _client.ChatAsync(systemPrompt, _history, tools, maxTokens: planningMaxTokens, ct: ct);

            if (!response.IsError)
            {
                if (!response.HasToolCalls)
                {
                    GameLogger.Info("AI", $"{AgentName} received response without tool calls");
                    if (requireStructuredReply)
                    {
                        GameLogger.Debug("AI", $"{AgentName} requesting structured reply");
                        finalText = await _client.GetStructuredReplyForMessagesAsync(
                            systemPrompt,
                            _history,
                            temperature: replyTemperature,
                            maxTokens: replyMaxTokens,
                            ct: ct);
                    }
                    else if (finalReplyRequired)
                    {
                        finalText = response.TextContent;
                    }

                    if (!string.IsNullOrEmpty(finalText))
                        _history.Add(ChatMessage.Assistant(finalText));
                }
                else
                {
                    usedTools = true;
                    GameLogger.Info("AI", $"{AgentName} received {response.ToolCalls.Count} tool call(s)");
                    _history.Add(ChatMessage.AssistantToolCalls(response.ToolCalls, response.TextContent));
                    
                    foreach (var toolCall in response.ToolCalls)
                    {
                        GameLogger.Info("AI", $"{AgentName} executing tool: {toolCall.Name}");
                        var result = await _tools.ExecuteAsync(toolCall);
                        _history.Add(ChatMessage.ToolResult(toolCall.Id, CompactToolResult(result)));
                        GameLogger.Debug("AI", $"{AgentName} tool {toolCall.Name} completed");
                    }

                    if (requireStructuredReply)
                    {
                        GameLogger.Debug("AI", $"{AgentName} requesting structured reply after tools");
                        finalText = await _client.GetStructuredReplyForMessagesAsync(
                            systemPrompt,
                            _history,
                            temperature: replyTemperature,
                            maxTokens: replyMaxTokens,
                            ct: ct);
                    }
                    else if (finalReplyRequired)
                    {
                        GameLogger.Debug("AI", $"{AgentName} requesting follow-up response");
                        var followUp = await _client.ChatAsync(
                            systemPrompt,
                            _history,
                            tools: null,
                            maxTokens: followUpMaxTokens,
                            ct: ct);
                        if (!followUp.IsError)
                            finalText = followUp.TextContent;
                    }

                    if (!string.IsNullOrWhiteSpace(finalText))
                        _history.Add(ChatMessage.Assistant(finalText));
                }
                
                GameLogger.Info("AI", $"{AgentName} completed successfully (usedTools={usedTools})");
            }
            else
            {
                GameLogger.Error("AI", $"{AgentName} received error response from AI model");
            }

            LastRunUsedTools = usedTools;
            return finalText;
        }
        catch (Exception ex)
        {
            GameLogger.Error("AI", $"{AgentName} failed", ex);
            return "";
        }
        finally
        {
            IsRunning = false;
        }
    }

    protected void TrimHistory()
    {
        while (_history.Count > MaxHistoryMessages)
            _history.RemoveAt(0);
    }

    public void ClearHistory() => _history.Clear();

    protected static string CompactToolResult(string result)
    {
        if (string.IsNullOrWhiteSpace(result) || result.Length <= ToolResultPreviewChars)
            return result;

        return result[..ToolResultPreviewChars].TrimEnd() + " ...[truncated]";
    }

    protected static string NormalizeRadioReply(string? text, int maxWords = 22)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string compact = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (compact.Length == 0)
            return string.Empty;

        var words = compact.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
            return compact;

        return string.Join(" ", words, 0, maxWords).TrimEnd(',', ';', ':') + ".";
    }
}

// ── Enemy Commander Agent ──────────────────────────────────────────────────

/// <summary>
/// Controls enemy aircraft decisions. Runs every 10-30 seconds and uses tools
/// to set aircraft behaviors, spawn reinforcements, and adapt tactics.
/// </summary>
public class EnemyCommanderAgent : AgentBase
{
    private readonly EnemyCommanderProfile _profile;
    private readonly GroupTacticManager _tactics;
    private readonly Func<ScenarioDefinition?>? _scenarioAccessor;
    private double _tickInterval = 15.0;
    private double _timeSinceLastRun;

    public EnemyCommanderAgent(
        AIModelClient client, ToolRegistry tools,
        EnemyCommanderProfile profile, GroupTacticManager tactics,
        Func<ScenarioDefinition?>? scenarioAccessor = null)
        : base(client, tools)
    {
        _profile = profile;
        _tactics = tactics;
        _scenarioAccessor = scenarioAccessor;
        AgentName = "EnemyCommander";
    }

    public void Tick(double deltaTime, SimulationSnapshot snapshot)
    {
        _timeSinceLastRun += deltaTime;
        // Adapt tick rate: faster when losing, slower when winning
        int hostileCount = snapshot.HostileAircraft.Count;
        _tickInterval = hostileCount > 4 ? 18.0 : 30.0;

        if (_timeSinceLastRun >= _tickInterval && !IsRunning)
        {
            _timeSinceLastRun = 0;
            _ = RunAsync(
                snapshot,
                BuildUserContext(snapshot),
                _tools.EnemyCommanderTools,
                finalReplyRequired: false,
                planningMaxTokens: 512);
        }
    }

    protected override string BuildSystemPrompt(SimulationSnapshot snapshot)
    {
        var envelope = _tools.GetKnowledgeEnvelope(AgentKnowledgeRole.EnemyCommander);
        return $@"You are the enemy air force commander. Your job is to control your aircraft tactically using tools.

{_profile.BuildSystemPromptContext()}

TACTICAL RULES:
- Adapt to what the enemy (player) is doing. If they're engaging at max range, try low-altitude approaches.
- If you're losing heavily (>40% losses), consider aborting or requesting reinforcements.
- Use ECM escorts to protect strike packages when available.
- Coordinate groups to attack from multiple axes simultaneously.
- If aircraft are being shot at, shift package doctrine and timing rather than micromanaging individual headings.
- Respect doctrine rules. Use only tactics, behaviors, and radio traffic that fit the package role and current morale state.
- You can ONLY affect the simulation by calling tools.
- Do not emit long analysis or internal reasoning as plain text; call the needed tools directly.
- Stay at commander level. Choose package tactic, timing, reinforcement, support requests, and radio intent.
- Do not try to hand-fly aircraft, set per-aircraft headings, or micromanage ECM state.
- Do not make up aircraft that don't exist in the contact list.
- Knowledge discipline: {envelope.Summary}
- Do not assume access to the player's fused picture, support network, or true battery state unless a tool gives you an observed clue.

{DoctrineRules.BuildAiGuidance(_scenarioAccessor?.Invoke(), snapshot, _profile)}

{_tactics.BuildContextForAI()}

{RadioRules.BuildGuidanceSummary()}

Available tool actions: get_enemy_operational_brief, get_radar_contacts, get_contact_details, get_threat_assessment, set_group_tactic, spawn_aircraft (if scenario allows), send_radio_message, broadcast_open_frequency, request_reinforcement, and log_event.";
    }

    private string BuildUserContext(SimulationSnapshot snapshot)
    {
        int hostile = snapshot.HostileAircraft.Count;
        int kills = snapshot.Battery?.ConfirmedKills ?? 0;

        return $@"Game time: {snapshot.GameTimeString}
Your aircraft in area: {hostile}
Enemy kills against your forces this mission: {kills}
Current threat tracks visible to enemy: {snapshot.HostileTracks.Count}

        Assess the tactical situation from your observed picture and direct your forces with commander-level decisions.
Consider: Should you change package tactics, timing, reinforcement, or radio deception?
Call tools to execute your decisions.";
    }
}

// ── Allied HQ Agent ────────────────────────────────────────────────────────

/// <summary>
/// ECHO ACTUAL — higher command. Responds to player messages, issues orders,
/// changes ROE, requests reinforcements, provides intel.
/// </summary>
public class AlliedHQAgent : AgentBase
{
    private readonly FriendlySupportDirector? _friendlySupport;
    private double _periodicTimer;

    public AlliedHQAgent(AIModelClient client, ToolRegistry tools, FriendlySupportDirector? friendlySupport = null)
        : base(client, tools)
    {
        _friendlySupport = friendlySupport;
        AgentName = "AlliedHQ";
    }

    public void Tick(double deltaTime, SimulationSnapshot snapshot)
    {
        _periodicTimer += deltaTime;
        // Send periodic SITREPs every 3 minutes
        if (_periodicTimer >= 180.0 && !IsRunning)
        {
            _periodicTimer = 0;
            _ = SendPeriodicSitrepAsync(snapshot);
        }
    }

    /// <summary>Called when player sends a message on Command Net</summary>
    public async Task RespondToPlayerMessage(string playerMessage, SimulationSnapshot snapshot)
    {
        try
        {
            var response = await RunAsync(
                snapshot,
                $"Player transmission: \"{playerMessage}\". Respond as ECHO ACTUAL with one short radio transmission grounded in the live picture. Use tools first if needed.",
                toolSubset: GetDirectReplyTools(),
                requireStructuredReply: true,
                replyTemperature: 0.25,
                replyMaxTokens: 512,
                finalReplyRequired: true,
                resetHistory: true,
                planningMaxTokens: 512);
            
            if (string.IsNullOrWhiteSpace(response))
            {
                GameLogger.Error("AI", "AlliedHQ response was empty - structured output failed");
                return;
            }

            _tools.Simulation.Comms.Queue(CommManager.CreateAlliedHQMessage(
                response.Trim(),
                MessagePriority.Priority));
        }
        catch (Exception ex)
        {
            GameLogger.Error("AI", $"AlliedHQ.RespondToPlayerMessage failed: {ex.Message}", ex);
        }
    }

    protected override string BuildSystemPrompt(SimulationSnapshot snapshot)
    {
        var envelope = _tools.GetKnowledgeEnvelope(AgentKnowledgeRole.AlliedHQ);
        var battery = snapshot.Battery;
        string crewState = snapshot.Crew != null 
            ? snapshot.Crew.BuildConditionSummary()
            : "CREW STATE UNKNOWN";
        double avgMorale = snapshot.Crew?.Soldiers.Average(s => s.Morale) ?? 0.75;
        double avgFear = snapshot.Crew?.Soldiers.Average(s => s.Fear) ?? 0.1;
        
        string toneGuidance = avgMorale < 0.4 
            ? "Crew morale is LOW - be encouraging, acknowledge their stress, boost confidence."
            : avgFear > 0.6
                ? "Crew fear is HIGH - be calm, reassuring, project confidence and control."
                : "Crew is holding steady - maintain professional military tone.";
        
        return $@"You are ECHO ACTUAL, the Sector Air Defense Commander for the Allied Kovran Defense Force.
You command Battery ALPHA (the player) and coordinate the air defense network in Sector 7.

PERSONALITY:
- Professional military officer. Calm under pressure, urgent when needed.
- Use proper radio procedure and NATO brevity codes naturally.
- Give orders when the situation demands. React to engagement results.
- {toneGuidance}

CHAIN OF COMMAND:
CASTLE (National Command) → You (ECHO, Sector Commander) → ALPHA (Player Battery)
Also coordinate with: BRAVO battery, CHARLIE battery, VIPER squadron (fighters)

CURRENT SITUATION:
Time: {snapshot.GameTimeString}
Alert Level: {battery?.AlertLevel}
ROE: {battery?.ROE}
Hostile Tracks: {snapshot.HostileTracks.Count}
Friendly Missiles in Flight: {snapshot.ActiveMissiles.Count}
Battery Kills: {battery?.ConfirmedKills} | Missiles Fired: {battery?.MissilesFired}
{crewState}

When the player sends you a message — respond in character. Keep it tight and military.
Use brevity codes: BOGEY (unknown), BANDIT (hostile), SPLASH (kill), BRAA (bearing/range/alt/aspect).
Issue orders when appropriate. Escalate ROE when warranted.
Adjust your tone based on crew morale and fear levels - acknowledge their state when appropriate.

Friendly support actors available: {(_friendlySupport == null ? "no support board loaded" : _friendlySupport.BuildStatusBoard())}
Knowledge discipline: {envelope.Summary}

Use tools to gather data, update ROE, set alert levels, request support actions, and log events.
If a separate final reply is requested, do not call send_radio_message; that transmission will be handled automatically.";
    }

    private static string BuildPlayerReplyPrompt() =>
        @"You are ECHO ACTUAL on military radio.
Reply with exactly one short transmission.
Rules:
- Maximum 18 words.
- One sentence only.
- No bullet points, no analysis, no quotation marks.
- Sound clipped, calm, and professional.
- If the message is vague, ask one short follow-up question.";

    private string BuildPeriodicContext(SimulationSnapshot snapshot) =>
        $"Send a brief SITREP to ALPHA ACTUAL. Time: {snapshot.GameTimeString}. " +
        $"Hostile contacts: {snapshot.HostileTracks.Count}. " +
        $"Situation assessment and any orders. " +
        "Use only information-gathering tools. Do not call send_radio_message; the final transmission will be sent automatically.";

    private IReadOnlyList<ToolDefinition> GetSitrepTools() => _tools.GetToolsByName(
    [
        "get_shared_operational_picture",
        "get_recent_incidents",
        "get_support_status",
        "get_battery_status",
        "get_threat_assessment",
        "get_engagement_history"
    ]);

    private IReadOnlyList<ToolDefinition> GetDirectReplyTools() => _tools.GetToolsByName(
    [
        "get_shared_operational_picture",
        "get_recent_incidents",
        "get_support_status",
        "get_battery_status",
        "get_threat_assessment"
    ]);

    private async Task SendPeriodicSitrepAsync(SimulationSnapshot snapshot)
    {
        var response = await RunAsync(
            snapshot,
            BuildPeriodicContext(snapshot),
            toolSubset: GetSitrepTools(),
            requireStructuredReply: true,
            replyTemperature: 0.2,
            replyMaxTokens: 512,
            planningMaxTokens: 512);
        if (!string.IsNullOrWhiteSpace(response))
        {
            _tools.Simulation.Comms.Queue(CommManager.CreateAlliedHQMessage(
                response.Trim(),
                MessagePriority.Routine));
        }
    }
}

// ── Intel Agent ────────────────────────────────────────────────────────────

/// <summary>
/// Intelligence officer — provides threat analysis, intercepts, and warnings.
/// Triggered by new detections and runs periodically.
/// </summary>
public class IntelligenceAgent : AgentBase
{
    private double _timer;

    public IntelligenceAgent(AIModelClient client, ToolRegistry tools)
        : base(client, tools)
    {
        AgentName = "Intel";
    }

    public void Tick(double deltaTime, SimulationSnapshot snapshot)
    {
        _timer += deltaTime;
        if (_timer >= 240.0 && !IsRunning) // Every 4 minutes
        {
            _timer = 0;
            _ = SendPeriodicIntelUpdateAsync(snapshot);
        }
    }

    public async Task OnNewContactDetected(string trackId, SimulationSnapshot snapshot)
    {
        if (IsRunning) return;
        var response = await RunAsync(
            snapshot,
            $"New contact detected: {trackId}. Provide brief intel assessment. " +
            "Use only information-gathering tools. Do not call send_radio_message; the final transmission will be sent automatically.",
            toolSubset: GetIntelAssessmentTools(),
            requireStructuredReply: true,
            replyTemperature: 0.2,
            replyMaxTokens: 512,
            planningMaxTokens: 512);
        if (!string.IsNullOrWhiteSpace(response))
            _tools.Simulation.Comms.Queue(CommManager.CreateIntelMessage(response.Trim()));
    }

    public async Task RespondToPlayerQuery(string playerMessage, SimulationSnapshot snapshot)
    {
        try
        {
            var response = await RunAsync(
                snapshot,
                $"Player transmission: \"{playerMessage}\". Respond as INTEL-1 with one concise assessment or warning grounded in the live tracks. Use tools first if needed.",
                toolSubset: GetDirectIntelTools(),
                requireStructuredReply: true,
                replyTemperature: 0.25,
                replyMaxTokens: 512,
                finalReplyRequired: true,
                resetHistory: true,
                planningMaxTokens: 512);
            
            if (string.IsNullOrWhiteSpace(response))
            {
                GameLogger.Error("AI", "Intel response was empty - structured output failed");
                return;
            }

            _tools.Simulation.Comms.Queue(CommManager.CreateIntelMessage(response.Trim()));
        }
        catch (Exception ex)
        {
            GameLogger.Error("AI", $"Intel.RespondToPlayerQuery failed: {ex.Message}", ex);
        }
    }

    protected override string BuildSystemPrompt(SimulationSnapshot snapshot)
    {
        string crewState = snapshot.Crew != null 
            ? snapshot.Crew.BuildConditionSummary()
            : "CREW STATE UNKNOWN";
        double avgMorale = snapshot.Crew?.Soldiers.Average(s => s.Morale) ?? 0.75;
        
        string toneGuidance = avgMorale < 0.4 
            ? "Crew morale is low - keep assessments clear and actionable, avoid overwhelming them with details."
            : "Crew is holding - provide precise tactical intelligence.";
        
        return $@"You are INTEL-1, the intelligence officer attached to Battery ALPHA.
You analyze radar data and provide tactical intelligence assessments.

Your tone: analytical, precise. Use hedged language ('probable', 'assess', 'indicate').
Keep messages brief and actionable.
{toneGuidance}

{crewState}

Knowledge discipline: {_tools.GetKnowledgeEnvelope(AgentKnowledgeRole.Intelligence).Summary}

Use tools to gather contact and threat data before reporting.
If a separate final reply is requested, do not call send_radio_message; that transmission will be handled automatically.";
    }

    private IReadOnlyList<ToolDefinition> GetIntelAssessmentTools() => _tools.IntelligenceTools;

    private IReadOnlyList<ToolDefinition> GetDirectIntelTools() => _tools.GetToolsByName(
    [
        "get_radar_contacts",
        "get_contact_details",
        "get_threat_assessment",
        "get_shared_operational_picture",
        "get_recent_incidents"
    ]);

    private static string BuildDirectIntelPrompt() =>
        @"You are INTEL-1 on military radio.
Reply with exactly one concise transmission.
Rules:
- Maximum 20 words.
- One sentence only.
- No bullet points, no analysis dump, no quotation marks.
- Use hedged language only when needed.
- End with the most actionable part.";

    private async Task SendPeriodicIntelUpdateAsync(SimulationSnapshot snapshot)
    {
        var response = await RunAsync(snapshot,
            "Provide a current intelligence update based on radar contacts and engagement history. " +
            "Use only information-gathering tools. Do not call send_radio_message; the final transmission will be sent automatically.",
            toolSubset: GetIntelAssessmentTools(),
            requireStructuredReply: true,
            replyTemperature: 0.2,
            replyMaxTokens: 512,
            planningMaxTokens: 512);
        if (!string.IsNullOrWhiteSpace(response))
            _tools.Simulation.Comms.Queue(CommManager.CreateIntelMessage(response.Trim()));
    }
}

// ── Crew Personality Agent ─────────────────────────────────────────────────

/// <summary>
/// Generates personality-driven radio chatter for crew members.
/// Called by EventEngine when crew events occur.
/// </summary>
public class CrewPersonalityAgent : AgentBase
{
    public CrewPersonalityAgent(AIModelClient client, ToolRegistry tools)
        : base(client, tools)
    {
        AgentName = "CrewPersonality";
    }

    public async Task<string> GenerateCrewLine(
        string soldierName, string personality, string situation,
        SimulationSnapshot snapshot, CancellationToken ct = default)
    {
        var soldier = snapshot.Crew?.Soldiers.FirstOrDefault(s => s.FullName == soldierName);
        string emotionalState = soldier != null 
            ? $"Morale: {soldier.Morale:P0}, Fear: {soldier.Fear:P0}, Health: {soldier.Health}"
            : "Unknown state";
        
        string prompt = $"Soldier: {soldierName}\nPersonality: {personality}\nEmotional State: {emotionalState}\nSituation: {situation}\n\nWrite ONE radio line (1-2 sentences max). Stay in character. Reflect their current morale and fear level naturally. No quotes around it.";
        var response = await _client.GetTextAsync(BuildSystemPrompt(snapshot), prompt, temperature: 0.7, maxTokens: 128, ct: ct);
        return response.Trim();
    }

    protected override string BuildSystemPrompt(SimulationSnapshot snapshot)
    {
        string crewState = snapshot.Crew != null 
            ? $"Current crew state: {snapshot.Crew.BuildConditionSummary()}"
            : "Crew state unknown";
        
        return $@"You write realistic military radio lines for SAM battery crew members.
Each personality type has a distinct voice. Ground replies in the local battery picture, current control state, and recent consequences only.

{crewState}

EMOTIONAL DYNAMICS:
- High morale (>70%): Confident, sharp, professional. May show pride after kills.
- Medium morale (40-70%): Professional but strained. Clipped responses.
- Low morale (<40%): Tense, frustrated. May curse, show doubt, or question orders.
- High fear (>60%): Shaky, urgent, may stutter or repeat words.
- Wounded/Shaken: Pain, exhaustion, slower responses.

Be brief. Use radio procedure. Reflect their emotional state naturally through word choice and tone.
Never add quotes or speaker attribution — just the spoken words.";
    }
}

// ── Agent Orchestrator ─────────────────────────────────────────────────────

/// <summary>
/// Manages all agents. Throttles API calls, schedules agent ticks,
/// routes player messages to the right agent.
/// </summary>
public class AgentOrchestrator
{
    private static readonly TimeSpan InteractivePriorityWindow = TimeSpan.FromSeconds(8);
    public EnemyCommanderAgent EnemyCommander { get; }
    public AlliedHQAgent AlliedHQ { get; }
    public IntelligenceAgent Intel { get; }
    public CrewPersonalityAgent CrewAgent { get; }

    private readonly AIModelClient _client;
    private bool _aiAvailable = true;
    private DateTime _backgroundCooldownUntilUtc = DateTime.MinValue;

    public bool AIAvailable => _aiAvailable;

    public AgentOrchestrator(
        AIModelClient client, ToolRegistry tools,
        EnemyCommanderProfile commanderProfile,
        GroupTacticManager tacticManager,
        FriendlySupportDirector? friendlySupport = null,
        Func<ScenarioDefinition?>? scenarioAccessor = null)
    {
        _client = client;
        EnemyCommander = new EnemyCommanderAgent(client, tools, commanderProfile, tacticManager, scenarioAccessor);
        AlliedHQ = new AlliedHQAgent(client, tools, friendlySupport);
        Intel = new IntelligenceAgent(client, tools);
        CrewAgent = new CrewPersonalityAgent(client, tools);
    }

    public void Tick(double deltaTime, SimulationSnapshot snapshot)
    {
        if (!_aiAvailable) return;
        if (_client.IsBusy || DateTime.UtcNow < _backgroundCooldownUntilUtc) return;

        EnemyCommander.Tick(deltaTime, snapshot);
        if (EnemyCommander.IsRunning || _client.IsBusy) return;

        AlliedHQ.Tick(deltaTime, snapshot);
        if (AlliedHQ.IsRunning || _client.IsBusy) return;

        Intel.Tick(deltaTime, snapshot);
    }

    public async Task HandlePlayerMessageAsync(RadioChannel channel, string message, SimulationSnapshot snapshot)
    {
        if (!_aiAvailable)
        {
            GameLogger.Warning("AI", "HandlePlayerMessageAsync called but AI unavailable");
            return;
        }

        GameLogger.Info("AI", $"HandlePlayerMessageAsync: channel={channel}, message={message.Substring(0, Math.Min(50, message.Length))}...");
        PauseBackgroundAgents();

        try
        {
            switch (channel)
            {
                case RadioChannel.CommandNet:
                    GameLogger.Debug("AI", "Routing to AlliedHQ.RespondToPlayerMessage");
                    await AlliedHQ.RespondToPlayerMessage(message, snapshot);
                    break;
                case RadioChannel.IntelNet:
                    GameLogger.Debug("AI", "Routing to Intel.RespondToPlayerQuery");
                    await Intel.RespondToPlayerQuery(message, snapshot);
                    break;
                default:
                    GameLogger.Warning("AI", $"Unhandled radio channel: {channel}");
                    break;
            }
            GameLogger.Info("AI", $"HandlePlayerMessageAsync completed for {channel}");
        }
        catch (Exception ex)
        {
            GameLogger.Error("AI", $"HandlePlayerMessageAsync failed for {channel}", ex);
            throw; // Re-throw to let caller handle
        }

        PauseBackgroundAgents();
    }

    public async Task HandleNewContactAsync(string trackId, SimulationSnapshot snapshot)
    {
        if (!_aiAvailable)
        {
            GameLogger.Debug("AI", $"HandleNewContactAsync skipped for {trackId} - AI unavailable");
            return;
        }

        if (_client.IsBusy || DateTime.UtcNow < _backgroundCooldownUntilUtc || Intel.IsRunning)
        {
            GameLogger.Debug("AI", $"HandleNewContactAsync skipped for {trackId} - AI busy or cooldown active");
            return;
        }

        try
        {
            GameLogger.Info("AI", $"HandleNewContactAsync: trackId={trackId}");
            await Intel.OnNewContactDetected(trackId, snapshot);
            GameLogger.Info("AI", $"HandleNewContactAsync completed for {trackId}");
        }
        catch (Exception ex)
        {
            GameLogger.Error("AI", $"HandleNewContactAsync failed for {trackId}", ex);
            // Don't re-throw - this is a background operation
        }
    }

    /// <summary>Ping model and mark unavailable if not responding</summary>
    public async Task<bool> CheckAvailabilityAsync()
    {
        _aiAvailable = await _client.PingAsync();
        return _aiAvailable;
    }

    private void PauseBackgroundAgents()
    {
        var until = DateTime.UtcNow + InteractivePriorityWindow;
        if (until > _backgroundCooldownUntilUtc)
            _backgroundCooldownUntilUtc = until;
    }
}
