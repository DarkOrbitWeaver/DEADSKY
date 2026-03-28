using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using DEADSKY.AI.Client;

namespace DEADSKY.Backend.Tests;

public class AIModelClientTests
{
    [Fact]
    public async Task ChatAsync_PreservesReasoningContent_WithoutSurfacingItAsAssistantText()
    {
        var handler = new StubHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "",
                    "reasoning_content": "ECHO, ALPHA. ROGER."
                  }
                }
              ],
              "usage": {
                "prompt_tokens": 10,
                "completion_tokens": 5
              }
            }
            """);

        var client = new AIModelClient(new HttpClient(handler));

        var response = await client.ChatAsync(
            "system",
            new List<ChatMessage> { ChatMessage.User("status") });

        Assert.Equal(string.Empty, response.TextContent);
        Assert.Equal("ECHO, ALPHA. ROGER.", response.ReasoningContent);
    }

    [Fact]
    public async Task ChatAsync_ReadsReasoning_FromNewLmStudioReasoningField()
    {
        var handler = new StubHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "",
                    "reasoning": "INTEL, ALPHA. THREAT AXIS EAST."
                  }
                }
              ]
            }
            """);

        var client = new AIModelClient(new HttpClient(handler));

        var response = await client.ChatAsync(
            "system",
            new List<ChatMessage> { ChatMessage.User("status") });

        Assert.Equal("INTEL, ALPHA. THREAT AXIS EAST.", response.ReasoningContent);
    }

    [Fact]
    public async Task GetStructuredJsonAsync_ReadsStructuredJson_FromLmStudioReasoningContent()
    {
        var handler = new StubHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "",
                    "reasoning_content": "{\"callsign\":\"INTEL-1\",\"priority\":\"priority\",\"text\":\"Threat east of sector.\"}"
                  }
                }
              ]
            }
            """);

        var client = new AIModelClient(new HttpClient(handler));
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["callsign"] = new JsonObject { ["type"] = "string" },
                ["priority"] = new JsonObject { ["type"] = "string" },
                ["text"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray("callsign", "priority", "text")
        };

        var json = await client.GetStructuredJsonAsync(
            "system",
            "Generate a packet.",
            "radio_packet",
            schema);

        Assert.NotNull(json);
        Assert.Equal("INTEL-1", json!["callsign"]!.GetValue<string>());
        Assert.Equal("priority", json["priority"]!.GetValue<string>());
        Assert.Equal("Threat east of sector.", json["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetTextAsync_ReturnsReply_FromStructuredOutputPayload()
    {
        var handler = new StubHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "{\"reply\":\"ALPHA, INTEL-1. PRIMARY RAID EAST AXIS.\"}"
                  }
                }
              ]
            }
            """);

        var client = new AIModelClient(new HttpClient(handler));

        var text = await client.GetTextAsync("system", "status");

        Assert.Equal("ALPHA, INTEL-1. PRIMARY RAID EAST AXIS.", text);
    }

    [Fact]
    public async Task GetTextAsync_RetriesStructuredOutput_WhenFirstPayloadIsEmpty()
    {
        var handler = new SequenceHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": ""
                  }
                }
              ]
            }
            """,
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "{\"reply\":\"ALPHA, ECHO. STAND BY FOR TASKING.\"}"
                  }
                }
              ]
            }
            """);

        var client = new AIModelClient(new HttpClient(handler));

        var text = await client.GetTextAsync("system", "status", maxTokens: 72);

        Assert.Equal("ALPHA, ECHO. STAND BY FOR TASKING.", text);
        Assert.Equal(2, handler.RequestBodies.Count);
        var retryBody = JsonNode.Parse(handler.RequestBodies[1]);
        Assert.NotNull(retryBody);
        Assert.Equal(256, retryBody!["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task GetTextAsync_SendsStructuredOutputSchema()
    {
        var handler = new SequenceHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "{\"reply\":\"Confirm target?\"}"
                  }
                }
              ]
            }
            """);

        var client = new AIModelClient(new HttpClient(handler));

        var text = await client.GetTextAsync("system", "hey", maxTokens: 72);

        Assert.Equal("Confirm target?", text);
        var requestBody = JsonNode.Parse(handler.RequestBodies[0]);
        Assert.NotNull(requestBody);
        Assert.Equal("json_schema", requestBody!["response_format"]!["type"]!.GetValue<string>());
        Assert.Equal("assistant_reply", requestBody["response_format"]!["json_schema"]!["name"]!.GetValue<string>());
        Assert.Equal("reply", requestBody["response_format"]!["json_schema"]!["schema"]!["required"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task GetStructuredReplyForMessagesAsync_PreservesConversationMessages()
    {
        var handler = new SequenceHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "{\"reply\":\"INTEL-1. PROBABLE STRIKE LEAD, EAST AXIS.\"}"
                  }
                }
              ]
            }
            """);

        var client = new AIModelClient(new HttpClient(handler));
        var messages = new List<ChatMessage>
        {
            ChatMessage.User("Assess new contact."),
            ChatMessage.AssistantToolCalls(new[]
            {
                new ToolCall
                {
                    Id = "call-1",
                    Name = "get_threat_assessment",
                    ArgumentsJson = "{}"
                }
            }),
            ChatMessage.ToolResult("call-1", "{\"contacts_count\":1}")
        };

        var reply = await client.GetStructuredReplyForMessagesAsync("system", messages, maxTokens: 90);

        Assert.Equal("INTEL-1. PROBABLE STRIKE LEAD, EAST AXIS.", reply);
        var requestBody = JsonNode.Parse(handler.RequestBodies[0]);
        Assert.NotNull(requestBody);
        Assert.Equal("assistant", requestBody!["messages"]![2]!["role"]!.GetValue<string>());
        Assert.Equal("tool", requestBody["messages"]![3]!["role"]!.GetValue<string>());
        Assert.Equal("get_threat_assessment", requestBody["messages"]![2]!["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ChatAsync_SanitizesPseudoToolContent_ForAllAgentPaths()
    {
        var handler = new StubHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "send_radio_message({\"channel\":\"command\",\"message\":\"ALPHA, ECHO. GO AHEAD.\"})"
                  }
                }
              ]
            }
            """);

        var client = new AIModelClient(new HttpClient(handler));

        var response = await client.ChatAsync("system", new List<ChatMessage> { ChatMessage.User("hey") });

        Assert.Equal("ALPHA, ECHO. GO AHEAD.", response.TextContent);
    }

    [Fact]
    public async Task ChatAsync_ParsesJsonPseudoToolCalls_WhenLmStudioLeavesThemInContent()
    {
        var handler = new StubHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "{\n  \"tool\": \"request_support_action\",\n  \"parameters\": {\n    \"package_id\": \"SUP-JAM\"\n  }\n}"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """);

        var client = new AIModelClient(new HttpClient(handler));
        var tools = new List<ToolDefinition>
        {
            new()
            {
                Name = "request_support_action",
                Description = "Activate support.",
                Parameters = new Dictionary<string, object>
                {
                    ["package_id"] = new { type = "string" }
                },
                RequiredParameters = new List<string> { "package_id" }
            }
        };

        var response = await client.ChatAsync(
            "system",
            new List<ChatMessage> { ChatMessage.User("jam now") },
            tools: tools);

        Assert.True(response.HasToolCalls);
        Assert.Equal("request_support_action", response.ToolCalls[0].Name);
        Assert.Equal("SUP-JAM", response.ToolCalls[0].GetString("package_id"));
        Assert.Equal(string.Empty, response.TextContent);
    }

    [Fact]
    public async Task ChatAsync_ParsesToolRequestBlocks_WhenToolsWereRequested()
    {
        var handler = new StubHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "[TOOL_REQUEST]{\"name\":\"get_threat_assessment\",\"arguments\":{}}[END_TOOL_REQUEST]"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """);

        var client = new AIModelClient(new HttpClient(handler));
        var tools = new List<ToolDefinition>
        {
            new()
            {
                Name = "get_threat_assessment",
                Description = "Return current threat.",
                Parameters = new Dictionary<string, object>(),
                RequiredParameters = new List<string>()
            }
        };

        var response = await client.ChatAsync(
            "system",
            new List<ChatMessage> { ChatMessage.User("status") },
            tools: tools);

        Assert.True(response.HasToolCalls);
        Assert.Equal("get_threat_assessment", response.ToolCalls[0].Name);
    }

    [Fact]
    public async Task ChatAsync_UsesCompactToolSchema_AndLowerDefaultToolBudget()
    {
        int originalToolBudget = AIModelClient.DefaultToolMaxTokens;
        AIModelClient.DefaultToolMaxTokens = 88;

        try
        {
            var handler = new SequenceHttpMessageHandler(
                """
                {
                  "choices": [
                    {
                      "message": {
                        "role": "assistant",
                        "content": "",
                        "tool_calls": []
                      }
                    }
                  ]
                }
                """);

            var client = new AIModelClient(new HttpClient(handler));
            var tools = new List<ToolDefinition>
            {
                new()
                {
                    Name = "request_support_action",
                    Description = "Request a friendly support actor action such as picture relay, CAP diversion, or relay recovery.",
                    Parameters = new Dictionary<string, object>
                    {
                        ["support_type"] = new { type = "string", description = "required: picture|declare|cap|jam|relay|battery|awacs|sar" },
                        ["details"] = new { type = "string", description = "required: support reason or cue" }
                    },
                    RequiredParameters = new List<string> { "support_type", "details" }
                }
            };

            await client.ChatAsync(
                "system",
                new List<ChatMessage> { ChatMessage.User("Need support") },
                tools: tools);

            var requestBody = JsonNode.Parse(handler.RequestBodies[0]);
            Assert.NotNull(requestBody);
            Assert.Equal(88, requestBody!["max_tokens"]!.GetValue<int>());
            Assert.Null(requestBody["tools"]![0]!["function"]!["parameters"]!["properties"]!["support_type"]!["description"]);
            Assert.Equal(
                "string",
                requestBody["tools"]![0]!["function"]!["parameters"]!["properties"]!["details"]!["type"]!.GetValue<string>());
        }
        finally
        {
            AIModelClient.DefaultToolMaxTokens = originalToolBudget;
        }
    }

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SequenceHttpMessageHandler(params string[] responseBodies) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responseBodies);
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            string responseBody = _responses.Count > 0
                ? _responses.Dequeue()
                : responseBodies[^1];

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
