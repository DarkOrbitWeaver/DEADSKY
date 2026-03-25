using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using DEADSKY.AI.Client;

namespace DEADSKY.Backend.Tests;

public class AIModelClientTests
{
    [Fact]
    public async Task ChatAsync_UsesReasoningContent_WhenContentIsBlank()
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

        Assert.Equal("ECHO, ALPHA. ROGER.", response.TextContent);
        Assert.Equal("ECHO, ALPHA. ROGER.", response.ReasoningContent);
    }

    [Fact]
    public async Task GetStructuredJsonAsync_ParsesJson_FromReasoningContentFallback()
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
        Assert.Equal("Threat east of sector.", json["text"]!.GetValue<string>());
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
}
