using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Threading;

namespace DEADSKY.AI.Client;

/// <summary>
/// Connects to the locally running AI model (Nemotron 3 Nano 4B or any OpenAI-compatible server).
/// Model is a simple global variable — change ModelIdentifier to switch models instantly.
/// </summary>
public class AIModelClient
{
    // ── GLOBAL MODEL CONFIGURATION ─────────────────────────────────────
    // Change this one variable to switch models for the entire game.
    public static string ModelIdentifier { get; set; } =
        Environment.GetEnvironmentVariable("DEADSKY_AI_MODEL") ?? "nvidia/nemotron-3-nano-4b";
    public static string ApiEndpoint { get; set; } =
        Environment.GetEnvironmentVariable("DEADSKY_AI_ENDPOINT") ?? "http://localhost:1234/v1/chat/completions";
    public static double DefaultTemperature { get; set; } = 0.25;
    public static int DefaultMaxTokens { get; set; } = 512;
    public static bool ReasoningEnabled { get; set; } =
        bool.TryParse(Environment.GetEnvironmentVariable("DEADSKY_AI_REASONING"), out var enabled) && enabled;
    public static string ModelsEndpoint => ApiEndpoint.Replace("/chat/completions", "/models", StringComparison.OrdinalIgnoreCase);
    // ──────────────────────────────────────────────────────────────────

    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOpts;
    private readonly SemaphoreSlim _requestGate = new(1, 1);

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
        requestMessages.AddRange(messages.Select(BuildChatMessagePayload));

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

        bool gateHeld = false;
        try
        {
            await _requestGate.WaitAsync(ct);
            gateHeld = true;
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
        finally
        {
            if (gateHeld)
                _requestGate.Release();
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

    /// <summary>
    /// Structured JSON output via LM Studio's OpenAI-compatible response_format support.
    /// Best used for small deterministic payloads, not large free-form reasoning.
    /// </summary>
    public async Task<JsonNode?> GetStructuredJsonAsync(
        string systemPrompt,
        string userMessage,
        string schemaName,
        JsonObject schema,
        bool strict = true,
        double temperature = 0.1,
        int maxTokens = 256,
        CancellationToken ct = default)
    {
        TotalRequestsMade++;

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = ModelIdentifier,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            ["response_format"] = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = schemaName,
                    strict = strict,
                    schema
                }
            },
            ["temperature"] = temperature,
            ["max_tokens"] = maxTokens,
            ["stream"] = false
        };

        bool gateHeld = false;
        try
        {
            await _requestGate.WaitAsync(ct);
            gateHeld = true;
            var response = await _http.PostAsJsonAsync(ApiEndpoint, requestBody, _jsonOpts, ct);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var message = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message");
            string content = ExtractMessageText(message);

            return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
        }
        catch (Exception ex)
        {
            TotalErrors++;
            Console.Error.WriteLine($"[AI] structured-output error: {ex.Message}");
            return null;
        }
        finally
        {
            if (gateHeld)
                _requestGate.Release();
        }
    }

    private static object BuildChatMessagePayload(ChatMessage message)
    {
        if (message.Role.Equals("tool", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                role = "tool",
                content = message.Content,
                tool_call_id = message.ToolCallId,
                name = message.Name
            };
        }

        if (message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) &&
            message.ToolCalls is { Count: > 0 })
        {
            return new
            {
                role = "assistant",
                content = string.IsNullOrWhiteSpace(message.Content) ? null : message.Content,
                tool_calls = message.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new
                    {
                        name = tc.Name,
                        arguments = tc.ArgumentsJson
                    }
                }).ToList()
            };
        }

        return new
        {
            role = message.Role.ToLowerInvariant(),
            content = message.Content
        };
    }

    private AIResponse ParseResponse(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var choice = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        var response = new AIResponse();

        if (choice.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null)
            response.TextContent = ExtractText(content);

        if (choice.TryGetProperty("reasoning_content", out var reasoning) &&
            reasoning.ValueKind != JsonValueKind.Null)
        {
            response.ReasoningContent = ExtractText(reasoning);
        }

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
            var resp = await _http.GetAsync(ModelsEndpoint,
                new CancellationTokenSource(3000).Token);
            IsAvailable = resp.IsSuccessStatusCode;
            return IsAvailable;
        }
        catch { IsAvailable = false; return false; }
    }

    private static string ExtractMessageText(JsonElement message)
    {
        if (message.TryGetProperty("content", out var content))
        {
            var text = ExtractText(content);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        if (message.TryGetProperty("reasoning_content", out var reasoning))
            return ExtractText(reasoning);

        return string.Empty;
    }

    private static string ExtractText(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(value.EnumerateArray().Select(ExtractTextPart)),
            JsonValueKind.Object => value.ToString(),
            _ => string.Empty
        };
    }

    private static string ExtractTextPart(JsonElement part)
    {
        if (part.ValueKind == JsonValueKind.String)
            return part.GetString() ?? string.Empty;

        if (part.ValueKind == JsonValueKind.Object &&
            part.TryGetProperty("text", out var textNode) &&
            textNode.ValueKind == JsonValueKind.String)
        {
            return textNode.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

// ── Data transfer objects ──────────────────────────────────────────────────

public class ChatMessage
{
    public string Role { get; set; } = "user";   // "user", "assistant", "tool"
    public string Content { get; set; } = "";
    public string? ToolCallId { get; set; }       // For tool result messages
    public string? Name { get; set; }             // For tool result messages
    public List<ToolCall>? ToolCalls { get; set; }

    public static ChatMessage User(string content) => new() { Role = "user", Content = content };
    public static ChatMessage Assistant(string content) => new() { Role = "assistant", Content = content };
    public static ChatMessage AssistantToolCalls(IEnumerable<ToolCall> toolCalls, string? content = null) => new()
    {
        Role = "assistant",
        Content = content ?? "",
        ToolCalls = toolCalls.ToList()
    };
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
    public string ReasoningContent { get; set; } = "";
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
        (v.ValueKind == System.Text.Json.JsonValueKind.Number ||
         v.ValueKind == System.Text.Json.JsonValueKind.String)
            ? TryGetDouble(v, defaultVal) : defaultVal;

    public int GetInt(string key, int defaultVal = 0) =>
        Arguments.TryGetValue(key, out var v) &&
        (v.ValueKind == System.Text.Json.JsonValueKind.Number ||
         v.ValueKind == System.Text.Json.JsonValueKind.String)
            ? TryGetInt(v, defaultVal) : defaultVal;

    private static double TryGetDouble(JsonElement value, double defaultVal)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetDouble();

        return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultVal;
    }

    private static int TryGetInt(JsonElement value, int defaultVal)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetInt32();

        return int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultVal;
    }
}

public class ToolDefinition
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public Dictionary<string, object> Parameters { get; init; } = new();
    public List<string> RequiredParameters { get; init; } = new();
}
