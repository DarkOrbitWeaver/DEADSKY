using DEADSKY.AI.Client;
using DEADSKY.AI.Tools;
using DEADSKY.Core.Campaign;
using DEADSKY.Core.Comms;
using DEADSKY.Core.EnemyAI;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.AI.Agents;

/// <summary>
/// Base class for all AI agents. Each agent has a system prompt persona,
/// conversation history, and access to specific tools.
/// </summary>
public abstract class AgentBase
{
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
        CancellationToken ct = default)
    {
        if (IsRunning) return "";
        IsRunning = true;
        LastRunTime = DateTime.UtcNow;

        try
        {
            var systemPrompt = BuildSystemPrompt(snapshot);
            var tools = (toolSubset ?? _tools.AllTools).ToList();

            // Add user context to history
            _history.Add(ChatMessage.User(userContext));
            TrimHistory();

            string finalText = "";
            bool usedTools = false;
            var response = await _client.ChatAsync(systemPrompt, _history, tools, ct: ct);

            if (!response.IsError)
            {
                if (!response.HasToolCalls)
                {
                    finalText = response.TextContent;
                    if (!string.IsNullOrEmpty(finalText))
                        _history.Add(ChatMessage.Assistant(finalText));
                }
                else
                {
                    usedTools = true;
                    _history.Add(ChatMessage.AssistantToolCalls(response.ToolCalls, response.TextContent));
                    foreach (var toolCall in response.ToolCalls)
                    {
                        var result = await _tools.ExecuteAsync(toolCall);
                        _history.Add(ChatMessage.ToolResult(toolCall.Id, result));
                    }

                    // LM Studio's recommended OpenAI-compatible flow is:
                    // first call with tools, then feed tool results back without tools enabled
                    // so the model returns a final assistant response instead of continuing tool loops.
                    var followUp = await _client.ChatAsync(systemPrompt, _history, tools: null, ct: ct);
                    if (!followUp.IsError)
                    {
                        finalText = followUp.TextContent;
                        if (!string.IsNullOrWhiteSpace(finalText))
                            _history.Add(ChatMessage.Assistant(finalText));
                    }
                }
            }

            LastRunUsedTools = usedTools;
            return finalText;
        }
        finally { IsRunning = false; }
    }

    protected void TrimHistory()
    {
        while (_history.Count > MaxHistoryMessages)
            _history.RemoveAt(0);
    }

    public void ClearHistory() => _history.Clear();

    protected static string NormalizeRadioReply(string? text, int maxWords = 22)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string compact = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (compact.Length == 0)
            return string.Empty;

        int sentenceBreak = compact.IndexOfAny(new[] { '.', '!', '?' });
        if (sentenceBreak >= 0 && sentenceBreak < compact.Length - 1)
            compact = compact[..(sentenceBreak + 1)].Trim();

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
            _ = RunAsync(snapshot, BuildUserContext(snapshot), _tools.EnemyCommanderTools);
        }
    }

    protected override string BuildSystemPrompt(SimulationSnapshot snapshot)
    {
        return $@"You are the enemy air force commander. Your job is to control your aircraft tactically using tools.

{_profile.BuildSystemPromptContext()}

TACTICAL RULES:
- Adapt to what the enemy (player) is doing. If they're engaging at max range, try low-altitude approaches.
- If you're losing heavily (>40% losses), consider aborting or requesting reinforcements.
- Use ECM escorts to protect strike packages when available.
- Coordinate groups to attack from multiple axes simultaneously.
- If aircraft are being shot at, tell them to evade or change flight path.
- Respect doctrine rules. Use only tactics, behaviors, and radio traffic that fit the package role and current morale state.
- You can ONLY affect the simulation by calling tools. Think step-by-step, then call tools.
- Stay at commander level. Choose package tactic, timing, reinforcement, support requests, and radio intent.
- Do not try to hand-fly aircraft, set per-aircraft headings, or micromanage ECM state.
- Do not make up aircraft that don't exist in the contact list.

{DoctrineRules.BuildAiGuidance(_scenarioAccessor?.Invoke(), snapshot, _profile)}

{_tactics.BuildContextForAI()}

{RadioRules.BuildGuidanceSummary()}

Available tool actions: set_group_tactic, spawn_aircraft (if scenario allows), send_radio_message, broadcast_open_frequency, get_support_status, request_reinforcement, request_support_action, cancel_support_action, log_event.";
    }

    private string BuildUserContext(SimulationSnapshot snapshot)
    {
        int hostile = snapshot.HostileAircraft.Count;
        int kills = snapshot.Battery?.ConfirmedKills ?? 0;

        return $@"Game time: {snapshot.GameTimeString}
Your aircraft in area: {hostile}
Enemy kills against your forces this mission: {kills}
Current threat tracks visible to enemy: {snapshot.HostileTracks.Count}

Assess the tactical situation and use tools to direct your forces. 
Consider: Should you change tactics? Adjust flight paths? Activate ECM? 
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
        if (IsRunning) return;
        IsRunning = true;
        LastRunTime = DateTime.UtcNow;

        try
        {
            string prompt = $@"Player transmission: ""{playerMessage}""
Time: {snapshot.GameTimeString}
Alert: {snapshot.Battery?.AlertLevel}
ROE: {snapshot.Battery?.ROE}
Hostile tracks: {snapshot.HostileTracks.Count}
Missiles in flight: {snapshot.ActiveMissiles.Count}

Reply as ECHO ACTUAL with one short radio transmission.";
            var response = await _client.GetTextAsync(
                BuildPlayerReplyPrompt(),
                prompt,
                temperature: 0.25,
                maxTokens: 72);

            response = NormalizeRadioReply(response, maxWords: 18);
            if (string.IsNullOrWhiteSpace(response))
                return;

            _tools.Simulation.Comms.Queue(CommManager.CreateAlliedHQMessage(
                response,
                MessagePriority.Priority));
        }
        finally
        {
            IsRunning = false;
        }
    }

    protected override string BuildSystemPrompt(SimulationSnapshot snapshot)
    {
        var battery = snapshot.Battery;
        return $@"You are ECHO ACTUAL, the Sector Air Defense Commander for the Allied Kovran Defense Force.
You command Battery ALPHA (the player) and coordinate the air defense network in Sector 7.

PERSONALITY:
- Professional military officer. Calm under pressure, urgent when needed.
- Use proper radio procedure and NATO brevity codes naturally.
- Give orders when the situation demands. React to engagement results.

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

When the player sends you a message — respond in character. Keep it tight and military.
Use brevity codes: BOGEY (unknown), BANDIT (hostile), SPLASH (kill), BRAA (bearing/range/alt/aspect).
Issue orders when appropriate. Escalate ROE when warranted.

Friendly support actors available: {(_friendlySupport == null ? "no support board loaded" : _friendlySupport.BuildStatusBoard())}

Use tools to send messages as ECHO ACTUAL, update ROE, set alert levels, request support actions, and log events.";
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
        $"Situation assessment and any orders.";

    private async Task SendPeriodicSitrepAsync(SimulationSnapshot snapshot)
    {
        var response = await RunAsync(snapshot, BuildPeriodicContext(snapshot));
        if (!LastRunUsedTools && !string.IsNullOrWhiteSpace(response))
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
        var response = await RunAsync(snapshot, $"New contact detected: {trackId}. Provide brief intel assessment.");
        if (!LastRunUsedTools && !string.IsNullOrWhiteSpace(response))
            _tools.Simulation.Comms.Queue(CommManager.CreateIntelMessage(response.Trim()));
    }

    public async Task RespondToPlayerQuery(string playerMessage, SimulationSnapshot snapshot)
    {
        if (IsRunning) return;
        IsRunning = true;
        LastRunTime = DateTime.UtcNow;

        try
        {
            string primary = snapshot.HostileTracks
                .OrderByDescending(track => track.ThreatLevel)
                .Select(track => $"{track.TrackId} {track.RangeNm:0.0}NM {track.AltitudeFt / 1000:0.0}kft")
                .FirstOrDefault() ?? "none";
            string prompt = $@"Player transmission: ""{playerMessage}""
Time: {snapshot.GameTimeString}
Hostile tracks: {snapshot.HostileTracks.Count}
Primary track: {primary}

Reply as INTEL-1 with one concise assessment or warning.";
            var response = await _client.GetTextAsync(
                BuildDirectIntelPrompt(),
                prompt,
                temperature: 0.2,
                maxTokens: 72);

            response = NormalizeRadioReply(response, maxWords: 20);
            if (string.IsNullOrWhiteSpace(response))
                return;

            _tools.Simulation.Comms.Queue(CommManager.CreateIntelMessage(response));
        }
        finally
        {
            IsRunning = false;
        }
    }

    protected override string BuildSystemPrompt(SimulationSnapshot snapshot) =>
        $@"You are INTEL-1, the intelligence officer attached to Battery ALPHA.
You analyze radar data and provide tactical intelligence assessments.

Your tone: analytical, precise. Use hedged language ('probable', 'assess', 'indicate').
Keep messages brief and actionable.

Use send_radio_message on the intel channel to deliver your reports to ALPHA.
Use get_radar_contacts and get_threat_assessment first to inform your analysis.";

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
            "Provide a current intelligence update based on radar contacts and engagement history.");
        if (!LastRunUsedTools && !string.IsNullOrWhiteSpace(response))
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
        string prompt = $"Soldier: {soldierName}\nPersonality: {personality}\nSituation: {situation}\n\nWrite ONE radio line (1-2 sentences max). Stay in character. No quotes around it.";
        var response = await _client.GetTextAsync(BuildSystemPrompt(snapshot), prompt, temperature: 0.7, maxTokens: 56, ct: ct);
        return NormalizeRadioReply(response, maxWords: 16);
    }

    protected override string BuildSystemPrompt(SimulationSnapshot snapshot) =>
        "You write realistic military radio lines for SAM battery crew members. " +
        "Each personality type has a distinct voice. Be brief. Use radio procedure. Keep each line under 16 words. " +
        "Never add quotes or speaker attribution — just the spoken words.";
}

// ── Agent Orchestrator ─────────────────────────────────────────────────────

/// <summary>
/// Manages all agents. Throttles API calls, schedules agent ticks,
/// routes player messages to the right agent.
/// </summary>
public class AgentOrchestrator
{
    public EnemyCommanderAgent EnemyCommander { get; }
    public AlliedHQAgent AlliedHQ { get; }
    public IntelligenceAgent Intel { get; }
    public CrewPersonalityAgent CrewAgent { get; }

    private readonly AIModelClient _client;
    private bool _aiAvailable = true;

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

        EnemyCommander.Tick(deltaTime, snapshot);
        AlliedHQ.Tick(deltaTime, snapshot);
        Intel.Tick(deltaTime, snapshot);
    }

    public async Task HandlePlayerMessageAsync(RadioChannel channel, string message, SimulationSnapshot snapshot)
    {
        if (!_aiAvailable) return;

        switch (channel)
        {
            case RadioChannel.CommandNet:
                await AlliedHQ.RespondToPlayerMessage(message, snapshot);
                break;
            case RadioChannel.IntelNet:
                await Intel.RespondToPlayerQuery(message, snapshot);
                break;
        }
    }

    public async Task HandleNewContactAsync(string trackId, SimulationSnapshot snapshot)
    {
        if (!_aiAvailable) return;
        await Intel.OnNewContactDetected(trackId, snapshot);
    }

    /// <summary>Ping model and mark unavailable if not responding</summary>
    public async Task<bool> CheckAvailabilityAsync()
    {
        _aiAvailable = await _client.PingAsync();
        return _aiAvailable;
    }
}
