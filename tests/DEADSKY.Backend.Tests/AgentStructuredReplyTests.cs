using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using DEADSKY.AI.Agents;
using DEADSKY.AI.Client;
using DEADSKY.AI.Tools;
using DEADSKY.Core.Comms;
using DEADSKY.Core.EnemyAI;

namespace DEADSKY.Backend.Tests;

public class AgentStructuredReplyTests
{
    [Fact]
    public async Task OnNewContactDetected_UsesInfoToolsAndQueuesStructuredIntelReply()
    {
        var sim = SimulationTestFactory.CreateLoadedSimulation();
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, rangeNm: 42);
        sim.RefreshSnapshot();

        var handler = new SequenceHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "",
                    "tool_calls": [
                      {
                        "id": "call-1",
                        "type": "function",
                        "function": {
                          "name": "get_threat_assessment",
                          "arguments": "{}"
                        }
                      }
                    ]
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
                    "content": "{\"reply\":\"INTEL-1, ALPHA. PROBABLE STRIKE LEAD, EAST AXIS.\"}"
                  }
                }
              ]
            }
            """);

        var client = new AIModelClient(new HttpClient(handler));
        var tools = new ToolRegistry(sim, new GroupTacticManager(sim.Entities));
        var agent = new IntelligenceAgent(client, tools);

        await agent.OnNewContactDetected(track.TrackId, sim.LatestSnapshot);

        var message = SimulationTestFactory.DrainSingleQueuedMessage(sim.Comms);

        Assert.Equal(RadioChannel.IntelNet, message.Channel);
        Assert.Contains("PROBABLE STRIKE LEAD", message.Content, StringComparison.OrdinalIgnoreCase);

        var firstRequest = JsonNode.Parse(handler.RequestBodies[0]);
        Assert.NotNull(firstRequest);
        Assert.DoesNotContain(
            firstRequest!["tools"]!.AsArray().Select(tool => tool!["function"]!["name"]!.GetValue<string>()),
            name => string.Equals(name, "send_radio_message", StringComparison.OrdinalIgnoreCase));

        var secondRequest = JsonNode.Parse(handler.RequestBodies[1]);
        Assert.NotNull(secondRequest);
        Assert.Equal("json_schema", secondRequest!["response_format"]!["type"]!.GetValue<string>());
        Assert.Equal(56, firstRequest["max_tokens"]!.GetValue<int>());
        Assert.Equal(64, secondRequest["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task OnNewContactDetected_ResetsConversationBetweenRuns()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        var track = SimulationTestFactory.AddDetectedHostileTrack(sim, rangeNm: 35);
        sim.RefreshSnapshot();

        var handler = new SequenceHttpMessageHandler(
            ToolCallResponse("get_threat_assessment", "{}"),
            StructuredReply("INTEL-1, ALPHA. PROBABLE STRIKE LEAD."),
            ToolCallResponse("get_threat_assessment", "{}"),
            StructuredReply("INTEL-1, ALPHA. RAID AXIS HOLDS EAST."));

        var client = new AIModelClient(new HttpClient(handler));
        var tools = new ToolRegistry(sim, new GroupTacticManager(sim.Entities));
        var agent = new IntelligenceAgent(client, tools);

        await agent.OnNewContactDetected(track.TrackId, sim.LatestSnapshot);
        SimulationTestFactory.DrainSingleQueuedMessage(sim.Comms);

        await agent.OnNewContactDetected(track.TrackId, sim.LatestSnapshot);
        SimulationTestFactory.DrainSingleQueuedMessage(sim.Comms);

        var firstRunRequest = JsonNode.Parse(handler.RequestBodies[0]);
        var secondRunRequest = JsonNode.Parse(handler.RequestBodies[2]);

        Assert.NotNull(firstRunRequest);
        Assert.NotNull(secondRunRequest);
        Assert.Equal(2, firstRunRequest!["messages"]!.AsArray().Count);
        Assert.Equal(2, secondRunRequest!["messages"]!.AsArray().Count);
    }

    [Fact]
    public async Task EnemyCommander_Tick_ExecutesToolPlan_WithoutFollowUpReplyRequest()
    {
        using var sim = SimulationTestFactory.CreateLoadedSimulation();
        SimulationTestFactory.AddDetectedHostileTrack(sim, designation: "MiG-29", rangeNm: 55);
        sim.RefreshSnapshot();

        var handler = new SequenceHttpMessageHandler(
            ToolCallResponse("get_threat_assessment", "{}"));

        var client = new AIModelClient(new HttpClient(handler));
        var tools = new ToolRegistry(sim, new GroupTacticManager(sim.Entities));
        var agent = new EnemyCommanderAgent(
            client,
            tools,
            EnemyCommanderProfile.ForChapter(1),
            new GroupTacticManager(sim.Entities));

        agent.Tick(30, sim.LatestSnapshot);

        await WaitUntilAsync(() => !agent.IsRunning && handler.RequestBodies.Count >= 1);

        Assert.Single(handler.RequestBodies);
        var request = JsonNode.Parse(handler.RequestBodies[0]);
        Assert.NotNull(request);
        Assert.Equal(96, request!["max_tokens"]!.GetValue<int>());
    }

    private static string ToolCallResponse(string toolName, string argumentsJson) =>
        $$"""
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": "",
                "tool_calls": [
                  {
                    "id": "call-1",
                    "type": "function",
                    "function": {
                      "name": "{{toolName}}",
                      "arguments": "{{argumentsJson.Replace("\"", "\\\"")}}"
                    }
                  }
                ]
              }
            }
          ]
        }
        """;

    private static string StructuredReply(string reply) =>
        $$"""
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": "{\"reply\":\"{{reply}}\"}"
              }
            }
          ]
        }
        """;

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 1500)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(25);
        }

        Assert.True(predicate(), "Condition was not met before timeout.");
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
