using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace DEADSKY.Backend.Tests;

/// <summary>
/// Tests to validate LM Studio structured output behavior and ensure 100% reliable JSON extraction.
/// Based on LM Studio documentation: https://www.lmstudio.ai/docs/developer/openai-compat/structured-output
/// </summary>
public class StructuredOutputTests
{
    private readonly ITestOutputHelper _output;
    private const string LM_STUDIO_ENDPOINT = "http://localhost:1234/v1/chat/completions";
    private const string MODEL_NAME = "nvidia/nemotron-3-nano-4b";

    public StructuredOutputTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Manual test - requires LM Studio running")]
    public async Task Test_BasicStructuredOutput_ReturnsValidJSON()
    {
        // Arrange
        var httpClient = new HttpClient();
        var requestBody = new
        {
            model = MODEL_NAME,
            messages = new[]
            {
                new { role = "system", content = "You are a helpful assistant." },
                new { role = "user", content = "Tell me a short joke." }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "joke_response",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            joke = new { type = "string" }
                        },
                        required = new[] { "joke" },
                        additionalProperties = false
                    }
                }
            },
            temperature = 0.7,
            max_tokens = 100,
            stream = false
        };

        // Act
        var response = await httpClient.PostAsJsonAsync(LM_STUDIO_ENDPOINT, requestBody);
        var responseText = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Response: {responseText}");

        // Assert
        Assert.True(response.IsSuccessStatusCode, $"Request failed: {response.StatusCode}");
        
        using var doc = JsonDocument.Parse(responseText);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        
        // Check where the content is
        bool hasContent = message.TryGetProperty("content", out var content) && 
                         content.ValueKind != JsonValueKind.Null &&
                         !string.IsNullOrWhiteSpace(content.GetString());
        
        bool hasReasoningContent = message.TryGetProperty("reasoning_content", out var reasoningContent) &&
                                  reasoningContent.ValueKind != JsonValueKind.Null &&
                                  !string.IsNullOrWhiteSpace(reasoningContent.GetString());

        _output.WriteLine($"Has content: {hasContent}");
        _output.WriteLine($"Has reasoning_content: {hasReasoningContent}");

        if (hasContent)
        {
            _output.WriteLine($"Content: {content.GetString()}");
            var parsed = JsonNode.Parse(content.GetString()!);
            Assert.NotNull(parsed);
            Assert.NotNull(parsed!["joke"]);
        }

        if (hasReasoningContent)
        {
            _output.WriteLine($"Reasoning content: {reasoningContent.GetString()}");
            var parsed = JsonNode.Parse(reasoningContent.GetString()!);
            Assert.NotNull(parsed);
            Assert.NotNull(parsed!["joke"]);
        }

        Assert.True(hasContent || hasReasoningContent, "Neither content nor reasoning_content contained the JSON");
    }

    [Fact(Skip = "Manual test - requires LM Studio running")]
    public async Task Test_StructuredOutputWithToolCalling_ReturnsValidJSON()
    {
        // Arrange
        var httpClient = new HttpClient();
        var requestBody = new
        {
            model = MODEL_NAME,
            messages = new[]
            {
                new { role = "system", content = "You are ECHO ACTUAL, a military commander. Respond with short radio transmissions." },
                new { role = "user", content = "Player transmission: 'Request picture'. Respond as ECHO ACTUAL with one short radio transmission." }
            },
            tools = new[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "get_shared_operational_picture",
                        description = "Get current operational picture with all tracks and threats",
                        parameters = new
                        {
                            type = "object",
                            properties = new { },
                            required = Array.Empty<string>()
                        }
                    }
                }
            },
            tool_choice = "auto",
            temperature = 0.25,
            max_tokens = 512,
            stream = false
        };

        // Act - First call (should return tool call)
        var response1 = await httpClient.PostAsJsonAsync(LM_STUDIO_ENDPOINT, requestBody);
        var responseText1 = await response1.Content.ReadAsStringAsync();
        _output.WriteLine($"First response: {responseText1}");

        Assert.True(response1.IsSuccessStatusCode);
        
        using var doc1 = JsonDocument.Parse(responseText1);
        var message1 = doc1.RootElement.GetProperty("choices")[0].GetProperty("message");
        Assert.True(message1.TryGetProperty("tool_calls", out var toolCalls));
        Assert.True(toolCalls.GetArrayLength() > 0);

        // Simulate tool execution
        var toolResult = "Air picture clean. No hostile contacts. SABLE-1 fighter offline.";

        // Act - Second call with structured output
        var requestBody2 = new
        {
            model = MODEL_NAME,
            messages = new object[]
            {
                new { role = "system", content = "You are ECHO ACTUAL, a military commander. Respond with short radio transmissions." },
                new { role = "user", content = "Player transmission: 'Request picture'. Respond as ECHO ACTUAL with one short radio transmission." },
                new
                {
                    role = "assistant",
                    content = (string?)null,
                    tool_calls = new[]
                    {
                        new
                        {
                            id = "test_tool_call_1",
                            type = "function",
                            function = new
                            {
                                name = "get_shared_operational_picture",
                                arguments = "{}"
                            }
                        }
                    }
                },
                new
                {
                    role = "tool",
                    tool_call_id = "test_tool_call_1",
                    content = toolResult
                }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "assistant_reply",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            reply = new { type = "string" }
                        },
                        required = new[] { "reply" },
                        additionalProperties = false
                    }
                }
            },
            temperature = 0.25,
            max_tokens = 512,
            stream = false
        };

        var response2 = await httpClient.PostAsJsonAsync(LM_STUDIO_ENDPOINT, requestBody2);
        var responseText2 = await response2.Content.ReadAsStringAsync();
        _output.WriteLine($"Second response: {responseText2}");

        // Assert
        Assert.True(response2.IsSuccessStatusCode);
        
        using var doc2 = JsonDocument.Parse(responseText2);
        var message2 = doc2.RootElement.GetProperty("choices")[0].GetProperty("message");
        
        // Extract JSON from either content or reasoning_content
        string? jsonContent = null;
        if (message2.TryGetProperty("content", out var content) && 
            content.ValueKind != JsonValueKind.Null &&
            !string.IsNullOrWhiteSpace(content.GetString()))
        {
            jsonContent = content.GetString();
            _output.WriteLine($"Found in content: {jsonContent}");
        }
        else if (message2.TryGetProperty("reasoning_content", out var reasoningContent) &&
                 reasoningContent.ValueKind != JsonValueKind.Null &&
                 !string.IsNullOrWhiteSpace(reasoningContent.GetString()))
        {
            jsonContent = reasoningContent.GetString();
            _output.WriteLine($"Found in reasoning_content: {jsonContent}");
        }

        Assert.NotNull(jsonContent);
        Assert.NotEmpty(jsonContent);

        // Parse and validate JSON
        var parsed = JsonNode.Parse(jsonContent);
        Assert.NotNull(parsed);
        Assert.NotNull(parsed!["reply"]);
        
        var reply = parsed["reply"]!.GetValue<string>();
        Assert.NotEmpty(reply);
        _output.WriteLine($"Extracted reply: {reply}");
    }

    [Fact]
    public void Test_JsonExtraction_FromReasoningContent()
    {
        // Arrange - Simulate LM Studio response with JSON in reasoning_content
        var responseJson = @"{
  ""id"": ""test"",
  ""object"": ""chat.completion"",
  ""created"": 1774617494,
  ""model"": ""nvidia/nemotron-3-nano-4b"",
  ""choices"": [
    {
      ""index"": 0,
      ""message"": {
        ""role"": ""assistant"",
        ""content"": """",
        ""reasoning_content"": ""{\n  \""reply\"": \""ALPHA, PICTURE CLEAR. SABLE-1 OFF GRID. MAINTAIN SEARCH.\""\n}\n"",
        ""tool_calls"": []
      },
      ""logprobs"": null,
      ""finish_reason"": ""stop""
    }
  ],
  ""usage"": {
    ""prompt_tokens"": 988,
    ""completion_tokens"": 32,
    ""total_tokens"": 1020
  }
}";

        // Act
        using var doc = JsonDocument.Parse(responseJson);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        
        string? extractedContent = null;
        if (message.TryGetProperty("content", out var content) && 
            content.ValueKind != JsonValueKind.Null &&
            !string.IsNullOrWhiteSpace(content.GetString()))
        {
            extractedContent = content.GetString();
        }
        else if (message.TryGetProperty("reasoning_content", out var reasoningContent) &&
                 reasoningContent.ValueKind != JsonValueKind.Null &&
                 !string.IsNullOrWhiteSpace(reasoningContent.GetString()))
        {
            extractedContent = reasoningContent.GetString();
        }

        // Assert
        Assert.NotNull(extractedContent);
        _output.WriteLine($"Extracted: {extractedContent}");
        
        var parsed = JsonNode.Parse(extractedContent);
        Assert.NotNull(parsed);
        Assert.NotNull(parsed!["reply"]);
        
        var reply = parsed["reply"]!.GetValue<string>();
        Assert.Equal("ALPHA, PICTURE CLEAR. SABLE-1 OFF GRID. MAINTAIN SEARCH.", reply);
    }
}
