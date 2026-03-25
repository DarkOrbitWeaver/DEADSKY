using DEADSKY.Core.Campaign;
using DEADSKY.Core.Radar;

namespace DEADSKY.Backend.Tests;

public class PackageDoctrineAdvisorTests
{
    [Fact]
    public void Build_UsesScenarioWaveMetadata_WhenTrackMatchesPackage()
    {
        var scenario = SimulationTestFactory.CreateOperationScenarioWithObjectives();
        scenario.EnemyForces.Waves[0].PackageName = "LANCER-1";
        scenario.EnemyForces.Waves[0].PackageRole = "fighter screen";
        scenario.EnemyForces.Waves[0].EntryLabel = "NORTH CAP";
        scenario.EnemyForces.Waves[0].TargetObjectiveId = "depot";

        var track = new TrackFile
        {
            TrackId = "TRK-0001",
            TrackDesignation = "MiG-29 Fulcrum",
            GroupLabel = "LANCER-1",
            Classification = TrackClassification.Hostile,
            ThreatLevel = 0.62,
            ClosingSpeedMps = 200
        };

        var advisory = PackageDoctrineAdvisor.Build(scenario, track);

        Assert.Contains("LANCER-1", advisory.PackageLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FIGHTER SCREEN", advisory.RoleLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NORTH CAP", advisory.RouteLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KOVRAN DEPOT", advisory.ObjectiveLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_FallsBackToTrackDrivenDoctrine_WhenNoWaveMatchExists()
    {
        var track = new TrackFile
        {
            TrackId = "TRK-0002",
            TrackDesignation = "Cruise Missile",
            GroupLabel = "UNATTRIBUTED",
            Classification = TrackClassification.Hostile,
            ThreatLevel = 0.9,
            ClosingSpeedMps = 280
        };

        var advisory = PackageDoctrineAdvisor.Build(null, track);

        Assert.Contains("UNATTRIBUTED", advisory.PackageLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MISSILE", advisory.RoleLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNKNOWN", advisory.ObjectiveLabel, StringComparison.OrdinalIgnoreCase);
    }
}
