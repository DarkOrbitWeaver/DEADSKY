using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    public static int DefaultToolMaxTokens { get; set; } = GetEnvInt("DEADSKY_AI_TOOL_MAX_TOKENS", 512);
    public static string ChatCompletionsEndpoint => ResolveEndpoint("/v1/chat/completions");
    public static string ModelsEndpoint => ResolveEndpoint("/v1/models");
    // ──────────────────────────────────────────────────────────────────

    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOpts;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private int _pendingOrActiveRequests;

    public bool IsAvailable { get; private set; } = true;
    public bool IsBusy => Volatile.Read(ref _pendingOrActiveRequests) > 0;
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
        int effectiveMaxTokens = maxTokens ?? (tools is { Count: > 0 } ? DefaultToolMaxTokens : DefaultMaxTokens);

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
            ["max_tokens"] = effectiveMaxTokens,
            ["stream"] = false
        };

        if (tools != null && tools.Count > 0)
        {
            requestBody["tools"] = tools.Select(BuildToolPayload).ToList();
            requestBody["tool_choice"] = "auto";
        }

        Console.WriteLine($"[AI] ChatAsync: messages={messages.Count}, tools={tools?.Count ?? 0}, maxTokens={effectiveMaxTokens}");

        bool gateHeld = false;
        try
        {
            Interlocked.Increment(ref _pendingOrActiveRequests);
            await _requestGate.WaitAsync(ct);
            gateHeld = true;
            var response = await _http.PostAsJsonAsync(ChatCompletionsEndpoint, requestBody, _jsonOpts, ct);
            response.EnsureSuccessStatusCode();

            var raw = await response.Content.ReadAsStringAsync(ct);
            var aiResponse = ParseResponse(raw, toolCallsExpected: tools is { Count: > 0 });
            
            Console.WriteLine($"[AI] ChatAsync response: finishReason={aiResponse.FinishReason}, promptTokens={aiResponse.PromptTokens}, completionTokens={aiResponse.CompletionTokens}, hasToolCalls={aiResponse.HasToolCalls}");
            
            return aiResponse;
        }
        catch (Exception ex)
        {
            TotalErrors++;
            IsAvailable = ex is not TaskCanceledException;
            Console.Error.WriteLine($"[AI] ChatAsync error: {ex.Message}");
            Console.Error.WriteLine($"[AI] Stack trace: {ex.StackTrace}");
            return new AIResponse { Error = ex.Message, IsError = true };
        }
        finally
        {
            if (gateHeld)
                _requestGate.Release();

            Interlocked.Decrement(ref _pendingOrActiveRequests);
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
        return await GetStructuredReplyForMessagesAsync(
            systemPrompt,
            new List<ChatMessage> { ChatMessage.User(userMessage) },
            temperature,
            maxTokens,
            ct);
    }

    public async Task<string> GetStructuredReplyForMessagesAsync(
        string systemPrompt,
        IReadOnlyList<ChatMessage> messages,
        double temperature = 0.25,
        int maxTokens = 256,
        CancellationToken ct = default)
    {
        JsonObject schema = BuildReplySchema();
        JsonNode? payload = await GetStructuredJsonForMessagesAsync(
            systemPrompt,
            messages,
            "assistant_reply",
            schema,
            strict: true,
            temperature: temperature,
            maxTokens: maxTokens,
            ct: ct);

        string reply = ExtractStructuredReply(payload);
        if (!string.IsNullOrWhiteSpace(reply))
            return reply;

        payload = await GetStructuredJsonForMessagesAsync(
            systemPrompt,
            messages,
            "assistant_reply",
            schema,
            strict: true,
            temperature: Math.Min(temperature, 0.35),
            maxTokens: Math.Max(maxTokens * 3, 160),
            ct: ct);

        return ExtractStructuredReply(payload);
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
        return await GetStructuredJsonForMessagesAsync(
            systemPrompt,
            new List<ChatMessage> { ChatMessage.User(userMessage) },
            schemaName,
            schema,
            strict,
            temperature,
            maxTokens,
            ct);
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

    private AIResponse ParseResponse(string raw, bool toolCallsExpected = false)
    {
        using var doc = JsonDocument.Parse(raw);
        var response = new AIResponse();
        var choice = doc.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        string rawContent = string.Empty;

        if (message.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null)
        {
            rawContent = ExtractText(content);
            response.TextContent = SanitizePlainAssistantText(rawContent);
        }

        response.ReasoningContent = ExtractReasoningText(message);

        if (message.TryGetProperty("tool_calls", out var toolCalls) &&
            toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in toolCalls.EnumerateArray())
            {
                var func = tc.GetProperty("function");
                response.ToolCalls.Add(CreateToolCall(
                    tc.TryGetProperty("id", out var tid) ? tid.GetString() ?? "" : "",
                    func.GetProperty("name").GetString() ?? "",
                    func.GetProperty("arguments").GetString() ?? "{}"));
            }
        }

        if (toolCallsExpected &&
            response.ToolCalls.Count == 0 &&
            TryExtractPseudoToolCalls(rawContent, out var pseudoToolCalls))
        {
            response.ToolCalls.AddRange(pseudoToolCalls);
            response.TextContent = ExtractToolEmbeddedMessage(rawContent);
        }

        response.FinishReason = choice.TryGetProperty("finish_reason", out var finishReason)
            ? finishReason.GetString() ?? string.Empty
            : string.Empty;

        // Log warning if response was truncated due to token limit
        if (response.FinishReason == "length")
        {
            Console.Error.WriteLine($"[AI] WARNING: Response truncated due to token limit. Completion tokens: {response.CompletionTokens}");
            Console.Error.WriteLine($"[AI] Consider increasing DefaultToolMaxTokens (currently {DefaultToolMaxTokens}) or max_tokens parameter");
        }

        // Extract usage
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            response.PromptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
            response.CompletionTokens = usage.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt32() : 0;
        }

        return response;
    }

    private async Task<JsonNode?> GetStructuredJsonForMessagesAsync(
        string systemPrompt,
        IReadOnlyList<ChatMessage> messages,
        string schemaName,
        JsonObject schema,
        bool strict,
        double temperature,
        int maxTokens,
        CancellationToken ct)
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
            Interlocked.Increment(ref _pendingOrActiveRequests);
            await _requestGate.WaitAsync(ct);
            gateHeld = true;
            var response = await _http.PostAsJsonAsync(ChatCompletionsEndpoint, requestBody, _jsonOpts, ct);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!TryExtractStructuredContent(doc.RootElement, out string content) ||
                string.IsNullOrWhiteSpace(content))
            {
                Console.Error.WriteLine($"[AI] structured-output: Failed to extract content from response");
                return null;
            }

            Console.WriteLine($"[AI] structured-output: Extracted content: {content.Substring(0, Math.Min(200, content.Length))}...");
            
            try
            {
                var parsed = JsonNode.Parse(content);
                if (parsed == null)
                {
                    Console.Error.WriteLine($"[AI] structured-output: JsonNode.Parse returned null for content: {content}");
                }
                return parsed;
            }
            catch (JsonException jsonEx)
            {
                Console.Error.WriteLine($"[AI] structured-output: JSON parsing failed: {jsonEx.Message}");
                Console.Error.WriteLine($"[AI] structured-output: Content was: {content}");
                return null;
            }
        }
        catch (Exception ex)
        {
            TotalErrors++;
            Console.Error.WriteLine($"[AI] structured-output error: {ex.Message}");
            Console.Error.WriteLine($"[AI] structured-output stack trace: {ex.StackTrace}");
            return null;
        }
        finally
        {
            if (gateHeld)
                _requestGate.Release();

            Interlocked.Decrement(ref _pendingOrActiveRequests);
        }
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

    private static bool TryExtractStructuredContent(JsonElement root, out string content)
    {
        content = string.Empty;
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return false;
        }

        var message = choices[0].GetProperty("message");
        if (message.TryGetProperty("content", out var contentNode))
        {
            var text = ExtractText(contentNode);
            if (!string.IsNullOrWhiteSpace(text))
            {
                content = text;
                return true;
            }
        }

        var reasoningText = ExtractReasoningText(message);
        if (!string.IsNullOrWhiteSpace(reasoningText))
        {
            content = reasoningText;
            return true;
        }

        return false;
    }

    private static string ExtractReasoningText(JsonElement message)
    {
        if (message.TryGetProperty("reasoning", out var reasoning) &&
            reasoning.ValueKind != JsonValueKind.Null)
        {
            return ExtractText(reasoning);
        }

        if (message.TryGetProperty("reasoning_content", out var reasoningContent) &&
            reasoningContent.ValueKind != JsonValueKind.Null)
        {
            return ExtractText(reasoningContent);
        }

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

    private static string SanitizePlainAssistantText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        string text = rawText.Trim();
        string extractedMessage = ExtractToolEmbeddedMessage(text);
        if (!string.IsNullOrWhiteSpace(extractedMessage))
            return extractedMessage;

        if (LooksLikeToolOnlyContent(text))
            return string.Empty;

        return text;
    }

    private static bool LooksLikeToolOnlyContent(string text)
    {
        if (text.Contains("<tool_call>", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("[TOOL_REQUEST]", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<function=", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<parameter=", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TryExtractPseudoToolCalls(text, out _))
        {
            return true;
        }

        return false;
    }

    private static string ExtractToolEmbeddedMessage(string text)
    {
        if (!text.Contains("message", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var jsonStyle = Regex.Match(text, "\"message\"\\s*:\\s*\"(?<msg>(?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase);
        if (jsonStyle.Success)
        {
            string encoded = jsonStyle.Groups["msg"].Value;
            try
            {
                return JsonSerializer.Deserialize<string>($"\"{encoded}\"")?.Trim() ?? string.Empty;
            }
            catch
            {
                return encoded.Trim();
            }
        }

        var tagStyle = Regex.Match(text, "<parameter=message>\\s*(?<msg>.*?)\\s*</parameter>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return tagStyle.Success ? tagStyle.Groups["msg"].Value.Trim() : string.Empty;
    }

    private static JsonObject BuildReplySchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["reply"] = new JsonObject
            {
                ["type"] = "string"
            }
        },
        ["required"] = new JsonArray("reply"),
        ["additionalProperties"] = false
    };

    private static string ExtractStructuredReply(JsonNode? payload)
    {
        if (payload?["reply"] is not JsonValue replyNode)
            return string.Empty;

        try
        {
            return replyNode.GetValue<string>().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static object BuildToolPayload(ToolDefinition tool) => new
    {
        type = "function",
        function = new
        {
            name = tool.Name,
            description = CompactDescription(tool.Description),
            parameters = new
            {
                type = "object",
                properties = BuildCompactToolProperties(tool.Parameters),
                required = tool.RequiredParameters
            }
        }
    };

    private static Dictionary<string, object> BuildCompactToolProperties(IReadOnlyDictionary<string, object> parameters)
    {
        var compact = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, schema) in parameters)
            compact[name] = BuildCompactToolParameterSchema(schema);

        return compact;
    }

    private static object BuildCompactToolParameterSchema(object schema)
    {
        JsonNode? node = JsonSerializer.SerializeToNode(schema);
        if (node is not JsonObject obj)
            return new JsonObject { ["type"] = "string" };

        var compact = new JsonObject();
        foreach (string key in new[] { "type", "enum", "items", "properties", "required", "additionalProperties" })
        {
            if (obj[key] is JsonNode value)
                compact[key] = value.DeepClone();
        }

        if (compact.Count == 0)
            compact["type"] = "string";

        return compact;
    }

    private static string CompactDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        string compact = string.Join(" ", description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 96 ? compact : compact[..96].TrimEnd() + "...";
    }

    private static bool TryExtractPseudoToolCalls(string text, out List<ToolCall> toolCalls)
    {
        toolCalls = new List<ToolCall>();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (string payload in ExtractTaggedToolPayloads(text, "[TOOL_REQUEST]", "[END_TOOL_REQUEST]"))
        {
            if (TryParseToolPayload(payload, out var toolCall))
                toolCalls.Add(toolCall);
        }

        foreach (string payload in ExtractTaggedToolPayloads(text, "<tool_call>", "</tool_call>"))
        {
            if (TryParseToolPayload(payload, out var toolCall))
                toolCalls.Add(toolCall);
        }

        if (toolCalls.Count == 0 && TryParseToolPayload(text.Trim(), out var jsonToolCall))
            toolCalls.Add(jsonToolCall);

        if (toolCalls.Count == 0 && TryParseFunctionStyleToolCall(text.Trim(), out var functionToolCall))
            toolCalls.Add(functionToolCall);

        return toolCalls.Count > 0;
    }

    private static IEnumerable<string> ExtractTaggedToolPayloads(string text, string startTag, string endTag)
    {
        int searchStart = 0;
        while (searchStart < text.Length)
        {
            int start = text.IndexOf(startTag, searchStart, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                yield break;

            start += startTag.Length;
            int end = text.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
                yield break;

            yield return text[start..end].Trim();
            searchStart = end + endTag.Length;
        }
    }

    private static bool TryParseFunctionStyleToolCall(string text, out ToolCall toolCall)
    {
        toolCall = new ToolCall();
        var match = Regex.Match(
            text,
            @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<args>\{.*\})\)\s*$",
            RegexOptions.Singleline);

        if (!match.Success)
            return false;

        string name = match.Groups["name"].Value;
        string argsJson = match.Groups["args"].Value;
        if (!TryValidateJsonObject(argsJson))
            return false;

        toolCall = CreateToolCall("", name, argsJson);
        return true;
    }

    private static bool TryParseToolPayload(string payload, out ToolCall toolCall)
    {
        toolCall = new ToolCall();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!TryReadToolName(root, out var toolName))
                return false;

            JsonElement arguments = root.TryGetProperty("parameters", out var parameters)
                ? parameters
                : root.TryGetProperty("arguments", out var args)
                    ? args
                    : default;

            string argsJson = arguments.ValueKind == JsonValueKind.Object
                ? arguments.GetRawText()
                : "{}";

            string id = root.TryGetProperty("id", out var idNode)
                ? idNode.GetString() ?? string.Empty
                : string.Empty;

            toolCall = CreateToolCall(id, toolName, argsJson);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadToolName(JsonElement root, out string toolName)
    {
        toolName = string.Empty;
        if (root.TryGetProperty("tool", out var toolNode) && toolNode.ValueKind == JsonValueKind.String)
        {
            toolName = toolNode.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(toolName);
        }

        if (root.TryGetProperty("name", out var nameNode) && nameNode.ValueKind == JsonValueKind.String)
        {
            toolName = nameNode.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(toolName);
        }

        if (root.TryGetProperty("function", out var functionNode) &&
            functionNode.ValueKind == JsonValueKind.Object &&
            functionNode.TryGetProperty("name", out var nestedName) &&
            nestedName.ValueKind == JsonValueKind.String)
        {
            toolName = nestedName.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(toolName);
        }

        return false;
    }

    private static bool TryValidateJsonObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static ToolCall CreateToolCall(string id, string name, string argumentsJson)
    {
        var call = new ToolCall
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            Name = name,
            ArgumentsJson = argumentsJson
        };

        try
        {
            call.Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(call.ArgumentsJson) ?? new();
        }
        catch
        {
            call.Arguments = new();
        }

        return call;
    }

    private static string ResolveEndpoint(string desiredPath)
    {
        string endpoint = ApiEndpoint.TrimEnd('/');
        string[] knownSuffixes =
        {
            "/v1/chat/completions",
            "/chat/completions",
            "/v1/responses",
            "/responses",
            "/v1/models",
            "/models"
        };

        foreach (string suffix in knownSuffixes)
        {
            if (endpoint.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return endpoint[..^suffix.Length] + desiredPath;
        }

        if (endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return endpoint + desiredPath[3..];

        return endpoint + desiredPath;
    }

    private static int GetEnvInt(string name, int fallback)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;
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
    public string FinishReason { get; set; } = "";
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
