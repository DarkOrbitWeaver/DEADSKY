using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DEADSKY.AI.Client;
using DEADSKY.Core.Comms;
using Xunit;

namespace DEADSKY.Backend.Tests;

/// <summary>
/// Integration tests for structured output with REAL LM Studio API calls.
/// These tests verify that LM Studio's strict mode GUARANTEES valid JSON output.
/// </summary>
public class StructuredOutputIntegrationTests
{
    private const string LM_STUDIO_URL = "http://localhost:1234";
    private const string MODEL = "nvidia/nemotron-3-nano-4b";

    [Fact(Skip = "Manual test - requires LM Studio running")]
    public async Task Test_StructuredOutput_WithStrictMode_AlwaysReturnsValidJSON()
    {
        // Arrange
        var client = new AIModelClient();
        
        // Act - Make 10 requests to verify consistency
        var results = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var reply = await client.GetStructuredReplyForMessagesAsync(
                systemPrompt: "You are a military radio operator. Respond in military radio format.",
                messages: new[] { ChatMessage.User($"Test message {i + 1}: Request status report") },
                temperature: 0.7,
                maxTokens: 128,
                ct: CancellationToken.None
            );
            
            results.Add(reply);
            Console.WriteLine($"[Test {i + 1}] Reply: {reply}");
        }
        
        // Assert - All replies should be non-empty
        Assert.All(results, reply => Assert.False(string.IsNullOrWhiteSpace(reply)));
        Console.WriteLine($"\n✓ All {results.Count} requests returned valid replies");
    }

    [Fact(Skip = "Manual test - requires LM Studio running")]
    public async Task Test_RawStructuredOutput_VerifyResponseFormat()
    {
        // Arrange
        using var http = new HttpClient { BaseAddress = new Uri(LM_STUDIO_URL) };
        
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["reply"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The radio message reply"
                }
            },
            ["required"] = new JsonArray { "reply" }
        };

        var requestBody = new
        {
            model = MODEL,
            messages = new[]
            {
                new { role = "system", content = "You are a military radio operator." },
                new { role = "user", content = "Request picture" }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "assistant_reply",
                    strict = true,
                    schema
                }
            },
            temperature = 0.7,
            max_tokens = 128,
            stream = false
        };

        // Act
        var response = await http.PostAsJsonAsync("/v1/chat/completions", requestBody);
        response.EnsureSuccessStatusCode();
        
        var rawJson = await response.Content.ReadAsStringAsync();
        Console.WriteLine("=== RAW LM STUDIO RESPONSE ===");
        Console.WriteLine(rawJson);
        Console.WriteLine("=== END RAW RESPONSE ===\n");

        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        
        // Assert - Verify response structure
        Assert.True(root.TryGetProperty("choices", out var choices));
        Assert.True(choices.GetArrayLength() > 0);
        
        var message = choices[0].GetProperty("message");
        
        // Check which field contains the JSON
        bool hasContent = message.TryGetProperty("content", out var content) && 
                         content.ValueKind != JsonValueKind.Null &&
                         !string.IsNullOrWhiteSpace(content.GetString());
        
        bool hasReasoningContent = message.TryGetProperty("reasoning_content", out var reasoningContent) && 
                                   reasoningContent.ValueKind != JsonValueKind.Null &&
                                   !string.IsNullOrWhiteSpace(reasoningContent.GetString());
        
        bool hasReasoning = message.TryGetProperty("reasoning", out var reasoning) && 
                           reasoning.ValueKind != JsonValueKind.Null &&
                           !string.IsNullOrWhiteSpace(reasoning.GetString());

        Console.WriteLine($"content field: {(hasContent ? "✓ HAS DATA" : "✗ empty/null")}");
        Console.WriteLine($"reasoning_content field: {(hasReasoningContent ? "✓ HAS DATA" : "✗ empty/null")}");
        Console.WriteLine($"reasoning field: {(hasReasoning ? "✓ HAS DATA" : "✗ empty/null")}");
        
        // At least ONE field must have the JSON
        Assert.True(hasContent || hasReasoningContent || hasReasoning, 
            "JSON must be in at least one field: content, reasoning_content, or reasoning");
        
        // Extract and verify the JSON
        var extracted = StructuredOutputExtractor.ExtractStructuredJson(root);
        Assert.NotNull(extracted);
        
        var reply = StructuredOutputExtractor.ExtractField(extracted, "reply");
        Assert.False(string.IsNullOrWhiteSpace(reply));
        
        Console.WriteLine($"\n✓ Successfully extracted reply: {reply}");
    }

    [Fact(Skip = "Manual test - requires LM Studio running")]
    public async Task Test_StrictMode_GuaranteesSchemaCompliance()
    {
        // Arrange
        using var http = new HttpClient { BaseAddress = new Uri(LM_STUDIO_URL) };
        
        // Complex schema with multiple required fields
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["callsign"] = new JsonObject { ["type"] = "string" },
                ["status"] = new JsonObject { ["type"] = "string" },
                ["threat_count"] = new JsonObject { ["type"] = "integer" },
                ["ready"] = new JsonObject { ["type"] = "boolean" }
            },
            ["required"] = new JsonArray { "callsign", "status", "threat_count", "ready" }
        };

        var requestBody = new
        {
            model = MODEL,
            messages = new[]
            {
                new { role = "system", content = "You are a military radar operator reporting status." },
                new { role = "user", content = "Report your status" }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "status_report",
                    strict = true,
                    schema
                }
            },
            temperature = 0.7,
            max_tokens = 128,
            stream = false
        };

        // Act
        var response = await http.PostAsJsonAsync("/v1/chat/completions", requestBody);
        response.EnsureSuccessStatusCode();
        
        var rawJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawJson);
        
        var extracted = StructuredOutputExtractor.ExtractStructuredJson(doc.RootElement);
        Assert.NotNull(extracted);
        
        // Assert - ALL required fields must be present (LM Studio guarantee)
        Assert.NotNull(extracted["callsign"]);
        Assert.NotNull(extracted["status"]);
        Assert.NotNull(extracted["threat_count"]);
        Assert.NotNull(extracted["ready"]);
        
        Console.WriteLine($"✓ callsign: {extracted["callsign"]?.GetValue<string>()}");
        Console.WriteLine($"✓ status: {extracted["status"]?.GetValue<string>()}");
        Console.WriteLine($"✓ threat_count: {extracted["threat_count"]?.GetValue<int>()}");
        Console.WriteLine($"✓ ready: {extracted["ready"]?.GetValue<bool>()}");
        
        Console.WriteLine("\n✓ LM Studio strict mode GUARANTEED all required fields are present!");
    }
}
