using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DEADSKY.AI.Client;

/// <summary>
/// Connects to the locally running AI model (Nemotron 3 Nano 4B or any OpenAI-compatible server).
/// Model is a simple global variable — change ModelIdentifier to switch models instantly.
/// </summary>
public class AIModelClient
{
    // ── GLOBAL MODEL CONFIGURATION ─────────────────────────────────────
    // Change this one variable to switch models for the entire game.
    public static string ModelIdentifier { get; set; } = "nemotron-mini-4b-instruct";
    public static string ApiEndpoint { get; set; } = "http://localhost:1234/v1/chat/completions";
    public static double DefaultTemperature { get; set; } = 0.7;
    public static int DefaultMaxTokens { get; set; } = 512;
    public static bool ReasoningEnabled { get; set; } = false; // Nemotron reasoning toggle
    // ──────────────────────────────────────────────────────────────────

    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOpts;

    public bool IsAvailable { get; private set; } = true;
    public int TotalRequestsMade { get; private set; }
    public int TotalErrors { get; private set; }

    public AIModelClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Send a chat request with optional tool definitions.
    /// Returns the raw response which may contain text or tool calls.
    /// </summary>
    public async Task<AIResponse> ChatAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        List<ToolDefinition>? tools = null,
        double? temperature = null,
        int? maxTokens = null,
        CancellationToken ct = default)
    {
        TotalRequestsMade++;

        var requestMessages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        requestMessages.AddRange(messages.Select(m => (object)new
        {
            role = m.Role.ToLower(),
            content = m.Content
        }));

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = ModelIdentifier,
            ["messages"] = requestMessages,
            ["temperature"] = temperature ?? DefaultTemperature,
            ["max_tokens"] = maxTokens ?? DefaultMaxTokens,
            ["stream"] = false
        };

        // Nemotron reasoning toggle
        if (ReasoningEnabled)
            requestBody["thinking"] = new { type = "enabled", budget_tokens = 256 };

        if (tools != null && tools.Count > 0)
        {
            requestBody["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = new
                    {
                        type = "object",
                        properties = t.Parameters,
                        required = t.RequiredParameters
                    }
                }
            }).ToList();
            requestBody["tool_choice"] = "auto";
        }

        try
        {
            var response = await _http.PostAsJsonAsync(ApiEndpoint, requestBody, _jsonOpts, ct);
            response.EnsureSuccessStatusCode();

            var raw = await response.Content.ReadAsStringAsync(ct);
            return ParseResponse(raw);
        }
        catch (Exception ex)
        {
            TotalErrors++;
            IsAvailable = ex is not TaskCanceledException;
            return new AIResponse { Error = ex.Message, IsError = true };
        }
    }

    /// <summary>
    /// Send a chat request and receive a plain text response (no tools).
    /// Fast path for simple NPC chatter.
    /// </summary>
    public async Task<string> GetTextAsync(
        string systemPrompt, string userMessage,
        double temperature = 0.8, int maxTokens = 256,
        CancellationToken ct = default)
    {
        var resp = await ChatAsync(systemPrompt, new List<ChatMessage>
        {
            new() { Role = "User", Content = userMessage }
        }, temperature: temperature, maxTokens: maxTokens, ct: ct);

        return resp.IsError ? "" : resp.TextContent;
    }

    private AIResponse ParseResponse(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var choice = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        var response = new AIResponse();

        if (choice.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null)
            response.TextContent = content.GetString() ?? "";

        if (choice.TryGetProperty("tool_calls", out var toolCalls) &&
            toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in toolCalls.EnumerateArray())
            {
                var func = tc.GetProperty("function");
                var call = new ToolCall
                {
                    Id = tc.TryGetProperty("id", out var tid) ? tid.GetString() ?? "" : "",
                    Name = func.GetProperty("name").GetString() ?? "",
                    ArgumentsJson = func.GetProperty("arguments").GetString() ?? "{}"
                };
                // Parse arguments
                try
                {
                    call.Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        call.ArgumentsJson, _jsonOpts) ?? new();
                }
                catch { call.Arguments = new(); }
                response.ToolCalls.Add(call);
            }
        }

        // Extract usage
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            response.PromptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
            response.CompletionTokens = usage.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt32() : 0;
        }

        return response;
    }

    public async Task<bool> PingAsync()
    {
        try
        {
            var resp = await _http.GetAsync(ApiEndpoint.Replace("/v1/chat/completions", "/v1/models"),
                new CancellationTokenSource(3000).Token);
            IsAvailable = resp.IsSuccessStatusCode;
            return IsAvailable;
        }
        catch { IsAvailable = false; return false; }
    }
}

// ── Data transfer objects ──────────────────────────────────────────────────

public class ChatMessage
{
    public string Role { get; set; } = "user";   // "user", "assistant", "tool"
    public string Content { get; set; } = "";
    public string? ToolCallId { get; set; }       // For tool result messages
    public string? Name { get; set; }             // For tool result messages

    public static ChatMessage User(string content) => new() { Role = "user", Content = content };
    public static ChatMessage Assistant(string content) => new() { Role = "assistant", Content = content };
    public static ChatMessage ToolResult(string toolCallId, string result) => new()
    {
        Role = "tool",
        ToolCallId = toolCallId,
        Content = result
    };
}

public class AIResponse
{
    public string TextContent { get; set; } = "";
    public List<ToolCall> ToolCalls { get; set; } = new();
    public bool HasToolCalls => ToolCalls.Count > 0;
    public bool IsError { get; set; }
    public string? Error { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
}

public class ToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ArgumentsJson { get; set; } = "{}";
    public Dictionary<string, System.Text.Json.JsonElement> Arguments { get; set; } = new();

    public string GetString(string key, string defaultVal = "") =>
        Arguments.TryGetValue(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString() ?? defaultVal : defaultVal;

    public double GetDouble(string key, double defaultVal = 0) =>
        Arguments.TryGetValue(key, out var v) &&
        (v.ValueKind == System.Text.Json.JsonValueKind.Number)
            ? v.GetDouble() : defaultVal;

    public int GetInt(string key, int defaultVal = 0) =>
        Arguments.TryGetValue(key, out var v) &&
        v.ValueKind == System.Text.Json.JsonValueKind.Number
            ? v.GetInt32() : defaultVal;
}

public class ToolDefinition
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public Dictionary<string, object> Parameters { get; init; } = new();
    public List<string> RequiredParameters { get; init; } = new();
}
