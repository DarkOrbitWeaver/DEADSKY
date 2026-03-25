using System.Net;
using System.Text;
using DEADSKY.AI.Client;
using DEADSKY.AI.Scenario;
using DEADSKY.Core.Scenario;

namespace DEADSKY.Backend.Tests;

public class RealisticScenarioGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_ValidStructuredPayload_ReturnsRealisticScenario()
    {
        var handler = new StubHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "{\"name\":\"Realistic // Test Shift\",\"description\":\"Structured generated test scenario.\",\"durationMinutes\":18,\"packages\":[{\"packageName\":\"LANCER-1\",\"role\":\"fighter screen\",\"designation\":\"MiG-29\",\"doctrineTags\":[\"escort\"],\"aircraftCount\":2,\"triggerMinutes\":0.4,\"entryLabel\":\"NORTHERN CAP\",\"bearingDeg\":30,\"rangeNm\":124,\"altitudeFt\":22000,\"headingDeg\":215,\"speedKts\":480,\"aggressiveness\":0.63,\"targetObjectiveId\":\"depot\",\"usesTerrainMasking\":false},{\"packageName\":\"HAMMER-2\",\"role\":\"strike\",\"designation\":\"SU-24\",\"doctrineTags\":[\"strike\"],\"aircraftCount\":2,\"triggerMinutes\":2.8,\"entryLabel\":\"RIVER CORRIDOR\",\"bearingDeg\":72,\"rangeNm\":118,\"altitudeFt\":17000,\"headingDeg\":240,\"speedKts\":435,\"aggressiveness\":0.72,\"targetObjectiveId\":\"depot\",\"usesTerrainMasking\":false}],\"objectives\":[{\"id\":\"depot\",\"name\":\"Kovran Depot\",\"description\":\"Fuel reserve.\",\"bearingDeg\":128,\"rangeNm\":58,\"importance\":\"primary\"}],\"weather\":{\"description\":\"Broken cloud\",\"visibilityNm\":60,\"cloudCeilingFt\":15000},\"theater\":{\"theaterName\":\"Kovran Lowlands\",\"objectiveCount\":1,\"openFrequencyTraffic\":true},\"support\":[{\"supportType\":\"picture_relay\",\"availability\":\"ready\"}],\"rewardFactors\":[\"theater_survival\",\"command_discipline\"]}"
                  }
                }
              ]
            }
            """);

        var generator = new RealisticScenarioGenerator(new AIModelClient(new HttpClient(handler)));

        var result = await generator.GenerateAsync("Kovran test theater");

        Assert.NotNull(result);
        Assert.False(result!.UsedFallback);
        Assert.True(result.Validation.IsValid);
        Assert.NotNull(result.Scenario);
        Assert.True(result.Scenario!.IsRealisticMode);
        Assert.Equal("Realistic // Test Shift", result.Scenario.Name);
        Assert.Equal(2, result.Scenario.EnemyForces.Waves.Count);
    }

    [Fact]
    public async Task GenerateAsync_InvalidObjectiveReferenceInReasoningContent_NormalizesAndReturnsScenario()
    {
        var handler = new StubHttpMessageHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "",
                    "reasoning_content": "{\"name\":\"KOVRAN LOWLANDS DEADSKY\",\"description\":\"Low-alt raid.\",\"durationMinutes\":12,\"packages\":[{\"packageName\":\"DECOY-01\",\"role\":\"decoy\",\"designation\":\"K-7 Phantom Skimmer\",\"doctrineTags\":[\"decoy\",\"low-altitude masking\"],\"aircraftCount\":3,\"triggerMinutes\":5,\"bearingDeg\":120,\"rangeNm\":45,\"altitudeFt\":800,\"headingDeg\":120,\"speedKts\":380,\"aggressiveness\":0.7,\"targetObjectiveId\":\"DECOY-01\",\"usesTerrainMasking\":true}],\"objectives\":[{\"id\":\"DEFEND-AIRFIELD\",\"name\":\"Kovran Airfield\",\"description\":\"Protect the airfield.\",\"bearingDeg\":120,\"rangeNm\":45,\"importance\":\"high\"},{\"id\":\"DEFEND-DEPOT\",\"name\":\"Kovran Depot\",\"description\":\"Protect the depot.\",\"bearingDeg\":128,\"rangeNm\":48,\"importance\":\"medium\"}],\"weather\":{\"description\":\"Clear skies\",\"visibilityNm\":30,\"cloudCeilingFt\":1200},\"theater\":{\"theaterName\":\"Kovran Lowlands\",\"objectiveCount\":2,\"openFrequencyTraffic\":true},\"support\":[{\"supportType\":\"network_coverage\",\"availability\":\"full\"}],\"rewardFactors\":[\"objective_defended\",\"enemy_package_neutralized\"]}"
                  }
                }
              ]
            }
            """);

        var generator = new RealisticScenarioGenerator(new AIModelClient(new HttpClient(handler)));

        var result = await generator.GenerateAsync("Kovran lowlands", allowFallback: false);

        Assert.NotNull(result);
        Assert.False(result!.UsedFallback);
        Assert.NotNull(result.Scenario);
        Assert.True(result.Validation.IsValid);
        Assert.DoesNotContain(result.Contract.Packages, package => package.TargetObjectiveId == "DECOY-01");
        Assert.All(result.Contract.Packages, package => Assert.False(string.IsNullOrWhiteSpace(package.TargetObjectiveId)));
    }

    [Fact]
    public void Materialize_ValidContract_ProducesPlayableScenario()
    {
        var contract = ScenarioContractValidator.FromScenario(SimulationTestFactory.CreateOperationScenarioWithObjectives());

        var scenario = RealisticScenarioMaterializer.Materialize(contract);

        Assert.True(scenario.IsRealisticMode);
        Assert.NotEmpty(scenario.SectorMap.Objectives);
        Assert.NotEmpty(scenario.EnemyForces.Waves);
        Assert.True(scenario.DurationMinutes >= 10);
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
