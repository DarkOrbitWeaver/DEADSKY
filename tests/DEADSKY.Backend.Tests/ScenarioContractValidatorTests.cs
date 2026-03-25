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
}
