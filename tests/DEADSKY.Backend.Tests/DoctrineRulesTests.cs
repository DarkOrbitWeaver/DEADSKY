using DEADSKY.Core.EnemyAI;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Scenario;

namespace DEADSKY.Backend.Tests;

public class DoctrineRulesTests
{
    [Fact]
    public void AssessWave_JammerEscortPackage_AuthorizesEcmBehavior()
    {
        var wave = new WaveConfig
        {
            PackageName = "SPECTER-3",
            PackageRole = "jammer push"
        };
        var aircraft = new List<Aircraft>
        {
            Aircraft.CreateFromType("EA-6", Affiliation.Hostile),
            Aircraft.CreateFromType("SU-24", Affiliation.Hostile)
        };

        var assessment = DoctrineRules.AssessWave(wave, aircraft, 0.1);

        Assert.Contains("JAMMER", assessment.Tags);
        Assert.Contains(AircraftBehavior.ECMStandoff, assessment.AuthorizedBehaviors);
        Assert.Contains(GroupTactic.TerrainMasking, assessment.AuthorizedGroupTactics);
    }

    [Fact]
    public void AssessWave_HighLosses_TriggersRetreatAndSurrenderWindow()
    {
        var wave = new WaveConfig
        {
            PackageName = "SHADE-2",
            PackageRole = "feint and strike"
        };
        var aircraft = new List<Aircraft>
        {
            Aircraft.CreateFromType("MQ-9", Affiliation.Hostile),
            Aircraft.CreateFromType("SU-24", Affiliation.Hostile)
        };

        var assessment = DoctrineRules.AssessWave(wave, aircraft, 0.72);

        Assert.True(assessment.PanicState);
        Assert.True(assessment.RecommendRetreat);
        Assert.True(assessment.PermitSurrenderTraffic);
    }
}
