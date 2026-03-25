using System.Text.Json;
using System.Text.Json.Nodes;
using DEADSKY.AI.Client;
using DEADSKY.Core.Scenario;

namespace DEADSKY.AI.Scenario;

public sealed record RealisticScenarioGenerationResult(
    ScenarioDefinition? Scenario,
    ScenarioContract Contract,
    ScenarioContractValidation Validation,
    bool UsedFallback,
    string Summary,
    IReadOnlyList<string> ValidationErrors,
    string? FailureReason,
    int RetryCount,
    string? RawContractJson);

public sealed class RealisticScenarioGenerator
{
    private readonly AIModelClient _client;

    public RealisticScenarioGenerator(AIModelClient client)
    {
        _client = client;
    }

    public async Task<RealisticScenarioGenerationResult?> GenerateAsync(
        string theaterContext,
        CancellationToken ct = default,
        bool allowFallback = false)
    {
        const int maxAttempts = 2;
        string? failureReason = null;
        List<string> validationErrors = new();
        ScenarioContract? bestContract = null;
        ScenarioContractValidation? bestValidation = null;
        string? rawContractJson = null;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            JsonNode? json = await _client.GetStructuredJsonAsync(
                BuildSystemPrompt(),
                BuildUserPrompt(theaterContext, attempt, validationErrors),
                "deadsky_realistic_scenario",
                BuildSchema(),
                strict: true,
                temperature: 0.2,
                maxTokens: 900,
                ct: ct);

            if (json == null)
            {
                failureReason = "AI returned no structured scenario payload.";
                continue;
            }

            rawContractJson = json.ToJsonString();
            var contract = json.Deserialize<ScenarioContract>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (contract == null)
            {
                failureReason = "AI payload could not be deserialized into a scenario contract.";
                continue;
            }

            contract = ScenarioContractNormalizer.Normalize(contract);
            var validation = ScenarioContractValidator.Validate(contract);

            bestContract = contract;
            bestValidation = validation;
            validationErrors = validation.Errors.Concat(validation.Warnings).ToList();

            if (validation.IsValid)
            {
                var scenario = RealisticScenarioMaterializer.Materialize(contract);
                return new RealisticScenarioGenerationResult(
                    scenario,
                    contract,
                    validation,
                    UsedFallback: false,
                    Summary: $"AI generated {contract.Packages.Count} package(s) over {contract.Objectives.Count} objective(s).",
                    ValidationErrors: validation.Warnings,
                    FailureReason: null,
                    RetryCount: attempt,
                    RawContractJson: rawContractJson);
            }

            failureReason = validation.Errors.Count > 0
                ? "AI output failed semantic scenario validation."
                : "AI output remained incomplete after normalization.";
        }

        if (!allowFallback)
        {
            if (bestContract == null || bestValidation == null)
                return null;

            return new RealisticScenarioGenerationResult(
                Scenario: null,
                bestContract,
                bestValidation,
                UsedFallback: false,
                Summary: "AI returned a scenario payload, but it could not be materialized safely.",
                ValidationErrors: validationErrors,
                FailureReason: failureReason,
                RetryCount: maxAttempts - 1,
                RawContractJson: rawContractJson);
        }

        var fallbackContract = BuildFallbackContract(theaterContext);
        var fallbackValidation = ScenarioContractValidator.Validate(fallbackContract);
        var fallbackScenario = RealisticScenarioMaterializer.Materialize(fallbackContract);
        return new RealisticScenarioGenerationResult(
            fallbackScenario,
            fallbackContract,
            fallbackValidation,
            UsedFallback: true,
            Summary: "Fallback realistic scenario generated because AI output was unavailable or invalid.",
            ValidationErrors: validationErrors,
            FailureReason: failureReason,
            RetryCount: maxAttempts,
            RawContractJson: rawContractJson);
    }

    private static string BuildSystemPrompt() =>
        """
        You generate realistic, battery-centered air defense scenarios for a surface-to-air missile game.
        Output only valid JSON matching the schema.
        Constraints:
        - Build a believable air-defense shift, not arcade missions.
        - Favor 2-4 named hostile packages with mixed roles.
        - At least one defended objective must exist.
        - Include doctrine tags that match the package role.
        - Enemy behaviors should feel like real raid planning: escorts, decoys, jammers, low-altitude masking, or strike timing.
        - Keep the scenario playable for a single SAM battery command crew.
        - Use the theater name and atmosphere from the user prompt.
        """;

    private static string BuildUserPrompt(string theaterContext, int attempt, IReadOnlyList<string> priorErrors)
    {
        if (attempt == 0 || priorErrors.Count == 0)
            return $"Generate one realistic DEADSKY scenario for this theater context: {theaterContext}. Keep it grounded, readable, and tactically varied.";

        string corrective = string.Join(" | ", priorErrors.Take(6));
        return $"Retry the scenario for this theater context: {theaterContext}. The previous payload failed because: {corrective}. Fix those issues and return only corrected JSON.";
    }

    private static JsonObject BuildSchema()
    {
        JsonObject packageSchema = new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["packageName"] = Str(),
                ["role"] = Str(),
                ["designation"] = Str(),
                ["doctrineTags"] = Arr(Str()),
                ["aircraftCount"] = Num(),
                ["triggerMinutes"] = Num(),
                ["entryLabel"] = Str(),
                ["bearingDeg"] = Num(),
                ["rangeNm"] = Num(),
                ["altitudeFt"] = Num(),
                ["headingDeg"] = Num(),
                ["speedKts"] = Num(),
                ["aggressiveness"] = Num(),
                ["targetObjectiveId"] = Str(),
                ["usesTerrainMasking"] = Bool()
            },
            ["required"] = new JsonArray("packageName", "role", "designation", "doctrineTags", "aircraftCount", "triggerMinutes", "bearingDeg", "rangeNm", "altitudeFt", "headingDeg", "speedKts", "aggressiveness", "targetObjectiveId", "usesTerrainMasking"),
            ["additionalProperties"] = false
        };

        JsonObject objectiveSchema = new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["id"] = Str(),
                ["name"] = Str(),
                ["description"] = Str(),
                ["bearingDeg"] = Num(),
                ["rangeNm"] = Num(),
                ["importance"] = Str()
            },
            ["required"] = new JsonArray("id", "name", "description", "bearingDeg", "rangeNm", "importance"),
            ["additionalProperties"] = false
        };

        JsonObject supportSchema = new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["supportType"] = Str(),
                ["availability"] = Str()
            },
            ["required"] = new JsonArray("supportType", "availability"),
            ["additionalProperties"] = false
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = Str(),
                ["description"] = Str(),
                ["durationMinutes"] = Num(),
                ["packages"] = Arr(packageSchema),
                ["objectives"] = Arr(objectiveSchema),
                ["weather"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["description"] = Str(),
                        ["visibilityNm"] = Num(),
                        ["cloudCeilingFt"] = Num()
                    },
                    ["required"] = new JsonArray("description", "visibilityNm", "cloudCeilingFt"),
                    ["additionalProperties"] = false
                },
                ["theater"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["theaterName"] = Str(),
                        ["objectiveCount"] = Num(),
                        ["openFrequencyTraffic"] = Bool()
                    },
                    ["required"] = new JsonArray("theaterName", "objectiveCount", "openFrequencyTraffic"),
                    ["additionalProperties"] = false
                },
                ["support"] = Arr(supportSchema),
                ["rewardFactors"] = Arr(Str())
            },
            ["required"] = new JsonArray("name", "description", "durationMinutes", "packages", "objectives", "weather", "theater", "support", "rewardFactors"),
            ["additionalProperties"] = false
        };
    }

    private static ScenarioContract BuildFallbackContract(string theaterContext) => new()
    {
        Name = "Realistic Mode // Sector Watch",
        Description = $"Generated fallback scenario for {theaterContext}. Multiple raid packages probe the sector under mixed weather and open-frequency pressure.",
        DurationMinutes = 22,
        Objectives = new List<ScenarioObjectiveContract>
        {
            new() { Id = "depot", Name = "Kovran Depot", Description = "Fuel and ordnance reserve complex.", BearingDeg = 126, RangeNm = 56, Importance = "primary" },
            new() { Id = "relay", Name = "Dunewatch Relay", Description = "Sector relay and picture fusion node.", BearingDeg = 82, RangeNm = 84, Importance = "secondary" }
        },
        Packages = new List<ScenarioPackageContract>
        {
            new()
            {
                PackageName = "LANCER-1",
                Role = "fighter screen",
                Designation = "MiG-29",
                DoctrineTags = new List<string> { "escort" },
                AircraftCount = 2,
                TriggerMinutes = 0.4,
                EntryLabel = "NORTHERN CAP",
                BearingDeg = 28,
                RangeNm = 126,
                AltitudeFt = 22000,
                HeadingDeg = 216,
                SpeedKts = 480,
                Aggressiveness = 0.62,
                TargetObjectiveId = "depot"
            },
            new()
            {
                PackageName = "SHADE-2",
                Role = "feint and strike",
                Designation = "MQ-9",
                DoctrineTags = new List<string> { "decoy", "terrain" },
                AircraftCount = 2,
                TriggerMinutes = 2.0,
                EntryLabel = "RIVER CORRIDOR",
                BearingDeg = 68,
                RangeNm = 118,
                AltitudeFt = 900,
                HeadingDeg = 240,
                SpeedKts = 265,
                Aggressiveness = 0.36,
                TargetObjectiveId = "depot",
                UsesTerrainMasking = true
            },
            new()
            {
                PackageName = "SPECTER-3",
                Role = "jammer push",
                Designation = "EA-6",
                DoctrineTags = new List<string> { "jammer" },
                AircraftCount = 1,
                TriggerMinutes = 5.2,
                EntryLabel = "EASTERN LOW ROUTE",
                BearingDeg = 108,
                RangeNm = 132,
                AltitudeFt = 11000,
                HeadingDeg = 248,
                SpeedKts = 425,
                Aggressiveness = 0.54,
                TargetObjectiveId = "relay"
            },
            new()
            {
                PackageName = "HAMMER-4",
                Role = "strike",
                Designation = "SU-24",
                DoctrineTags = new List<string> { "strike" },
                AircraftCount = 2,
                TriggerMinutes = 6.4,
                EntryLabel = "SOUTHWEST ARC",
                BearingDeg = 214,
                RangeNm = 122,
                AltitudeFt = 17000,
                HeadingDeg = 34,
                SpeedKts = 438,
                Aggressiveness = 0.74,
                TargetObjectiveId = "depot"
            }
        },
        Weather = new ScenarioWeatherContract
        {
            Description = "Broken cloud",
            VisibilityNm = 58,
            CloudCeilingFt = 14500
        },
        Theater = new ScenarioTheaterContract
        {
            TheaterName = "Kovran Lowlands",
            ObjectiveCount = 2,
            OpenFrequencyTraffic = true
        },
        Support = new List<ScenarioSupportContract>
        {
            new() { SupportType = "picture_relay", Availability = "ready" },
            new() { SupportType = "cap", Availability = "conditional" },
            new() { SupportType = "awacs", Availability = "ready" }
        },
        RewardFactors = new List<string>
        {
            "theater_survival",
            "command_discipline",
            "intercept_success",
            "logistics_preservation",
            "crew_condition"
        }
    };

    private static JsonObject Str() => new() { ["type"] = "string" };
    private static JsonObject Num() => new() { ["type"] = "number" };
    private static JsonObject Bool() => new() { ["type"] = "boolean" };
    private static JsonObject Arr(JsonNode item) => new() { ["type"] = "array", ["items"] = item };
}
