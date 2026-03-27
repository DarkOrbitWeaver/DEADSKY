using DEADSKY.Core.Scenario;

namespace DEADSKY.Backend.Tests;

public class ScenarioContractValidatorTests
{
    [Fact]
    public void ValidateScenario_BuiltInScenario_ReturnsValidContract()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();

        var validation = ScenarioContractValidator.ValidateScenario(scenario);

        Assert.True(validation.IsValid);
    }

    [Fact]
    public void Validate_EmptyContract_ReturnsErrors()
    {
        var contract = new ScenarioContract();

        var validation = ScenarioContractValidator.Validate(contract);

        Assert.False(validation.IsValid);
        Assert.NotEmpty(validation.Errors);
    }

    [Fact]
    public void ValidateScenario_UnknownLandmarkCategory_ReturnsInvalidContract()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        scenario.SectorMap.Landmarks.Add(new MapLandmarkConfig
        {
            Name = "Broken Marker",
            BearingDeg = 30,
            RangeNm = 40,
            Category = "lava"
        });

        var validation = ScenarioContractValidator.ValidateScenario(scenario);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("unsupported category", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateScenario_UnknownObjectiveImportance_ReturnsInvalidContract()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        scenario.SectorMap.Objectives[0].Importance = "tertiary";

        var validation = ScenarioContractValidator.ValidateScenario(scenario);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("unsupported importance", StringComparison.OrdinalIgnoreCase));
    }
}
