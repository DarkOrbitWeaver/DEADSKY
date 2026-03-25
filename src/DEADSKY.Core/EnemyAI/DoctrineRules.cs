using System.Text;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.EnemyAI;

public sealed record EngagementRulesProfile
{
    public string ProfileName { get; init; } = "MainGame";
    public bool AllowOpenFrequencyWarnings { get; init; } = true;
    public bool AllowOpenFrequencySurrender { get; init; } = true;
    public bool ForceProfessionalFriendlyNets { get; init; } = true;
    public double RetreatLossThreshold { get; init; } = 0.45;
    public double PanicLossThreshold { get; init; } = 0.65;
    public double TerrainMaskAltitudeFt { get; init; } = 1200;

    public string BuildSummary() =>
        $"PROFILE {ProfileName}: terrain masking below {TerrainMaskAltitudeFt:0}FT, retreat above {RetreatLossThreshold:P0} losses, panic above {PanicLossThreshold:P0} losses.";

    public static EngagementRulesProfile MainGame { get; } = new();
}

public sealed record DoctrineAssessment(
    IReadOnlyList<string> Tags,
    IReadOnlyList<AircraftBehavior> AuthorizedBehaviors,
    IReadOnlyList<GroupTactic> AuthorizedGroupTactics,
    string FormationRule,
    string SupportRule,
    string MoraleRule,
    string RadioRule,
    bool EscortRequired,
    bool PermitSurrenderTraffic,
    bool RecommendRetreat,
    bool PanicState);

public static class DoctrineRules
{
    public static DoctrineAssessment AssessWave(
        WaveConfig? wave,
        IReadOnlyList<Aircraft> aircraft,
        double lossRatio,
        EngagementRulesProfile? profile = null)
    {
        profile ??= EngagementRulesProfile.MainGame;
        var tags = BuildTags(wave, aircraft);
        bool jammerPackage = tags.Contains("JAMMER");
        bool decoyPackage = tags.Contains("DECOY");
        bool seadPackage = tags.Contains("SEAD");
        bool strikePackage = tags.Contains("STRIKE");
        bool fighterEscort = aircraft.Any(a => a.Role == AircraftRole.Fighter);
        bool escortRequired = strikePackage && fighterEscort;
        bool panicState = lossRatio >= profile.PanicLossThreshold;
        bool retreat = lossRatio >= profile.RetreatLossThreshold || panicState;

        var behaviors = new HashSet<AircraftBehavior>
        {
            AircraftBehavior.IngressAttack,
            AircraftBehavior.EgressRetreat,
            AircraftBehavior.EvasiveManeuver
        };
        if (jammerPackage) behaviors.Add(AircraftBehavior.ECMStandoff);
        if (decoyPackage) behaviors.Add(AircraftBehavior.Feint);
        if (seadPackage) behaviors.Add(AircraftBehavior.SEAD);
        if (strikePackage) behaviors.Add(AircraftBehavior.PopUpAttack);
        if (tags.Contains("LOW_ALT")) behaviors.Add(AircraftBehavior.TerrainFollowing);
        if (aircraft.Any(a => a.Role == AircraftRole.Recon)) behaviors.Add(AircraftBehavior.OrbitPatrol);

        var groupTactics = new HashSet<GroupTactic> { GroupTactic.StraightIngress };
        if (escortRequired) groupTactics.Add(GroupTactic.ECMEscortPackage);
        if (decoyPackage) groupTactics.Add(GroupTactic.FeintAndStrike);
        if (jammerPackage) groupTactics.Add(GroupTactic.TerrainMasking);
        if (seadPackage) groupTactics.Add(GroupTactic.SEAD);
        if (fighterEscort && strikePackage) groupTactics.Add(GroupTactic.PincerAttack);
        if (panicState) groupTactics.Add(GroupTactic.TimeOnTarget);

        string morale = panicState
            ? "Morale broken. Survivors may scatter, plead on open frequency, or abort."
            : retreat
                ? "Losses unacceptable. Preserve surviving package and disengage in sequence."
                : "Maintain coordinated pressure until defensive fire forces a break.";

        string support = jammerPackage
            ? "Jammer remains offset and protects the main raid unless directly threatened."
            : seadPackage
                ? "SEAD elements probe radar emissions and bait the battery into revealing itself."
                : escortRequired
                    ? "Escort fighters screen the strikers and delay their own attack if needed."
                    : "Package uses organic spacing and route discipline without dedicated support.";

        string formation = tags.Contains("LOW_ALT")
            ? "Staggered low-altitude ingress with terrain separation."
            : escortRequired
                ? "Escorts lead or bracket the strikers until terminal phase."
                : "Loose tactical formation with coordinated spacing.";

        string radio = panicState
            ? "Internal net discipline degrades. Open-frequency panic or surrender traffic becomes plausible."
            : retreat
                ? "Internal traffic stays clipped, with possible warnings or diversion calls."
                : "Enemy nets remain disciplined; open-frequency taunts or warnings are situational only.";

        return new DoctrineAssessment(
            Tags: tags,
            AuthorizedBehaviors: behaviors.ToList(),
            AuthorizedGroupTactics: groupTactics.ToList(),
            FormationRule: formation,
            SupportRule: support,
            MoraleRule: morale,
            RadioRule: radio,
            EscortRequired: escortRequired,
            PermitSurrenderTraffic: profile.AllowOpenFrequencySurrender && retreat,
            RecommendRetreat: retreat,
            PanicState: panicState);
    }

    public static bool IsBehaviorAuthorized(Aircraft aircraft, AircraftBehavior behavior)
    {
        return aircraft.Role switch
        {
            AircraftRole.ECMEscort => behavior is AircraftBehavior.ECMStandoff or AircraftBehavior.EgressRetreat or AircraftBehavior.EvasiveManeuver or AircraftBehavior.TerrainFollowing,
            AircraftRole.SEAD => behavior is AircraftBehavior.SEAD or AircraftBehavior.PopUpAttack or AircraftBehavior.EgressRetreat or AircraftBehavior.EvasiveManeuver,
            AircraftRole.Recon => behavior is AircraftBehavior.OrbitPatrol or AircraftBehavior.Feint or AircraftBehavior.TerrainFollowing or AircraftBehavior.EgressRetreat,
            AircraftRole.Decoy => behavior is AircraftBehavior.Feint or AircraftBehavior.TerrainFollowing or AircraftBehavior.EgressRetreat,
            AircraftRole.Striker => behavior is AircraftBehavior.IngressAttack or AircraftBehavior.PopUpAttack or AircraftBehavior.TerrainFollowing or AircraftBehavior.EgressRetreat or AircraftBehavior.EvasiveManeuver,
            _ => behavior is not AircraftBehavior.SEAD || aircraft.HasARMCapability
        };
    }

    public static bool IsGroupTacticAuthorized(GroupTactic tactic, IReadOnlyList<Aircraft> aircraft)
    {
        bool hasJammer = aircraft.Any(a => a.HasECM || a.Role == AircraftRole.ECMEscort);
        bool hasSead = aircraft.Any(a => a.Role == AircraftRole.SEAD || a.HasARMCapability);
        bool hasStriker = aircraft.Any(a => a.Role == AircraftRole.Striker);
        bool hasDrone = aircraft.Any(a => a.Type == EntityType.Drone || a.Role == AircraftRole.Decoy);

        return tactic switch
        {
            GroupTactic.ECMEscortPackage => hasJammer && hasStriker,
            GroupTactic.SEAD => hasSead,
            GroupTactic.DroneDecoy => hasDrone,
            GroupTactic.TerrainMasking => aircraft.Any(a => a.Role is AircraftRole.Striker or AircraftRole.Decoy),
            _ => true
        };
    }

    public static GroupTactic RecommendGroupTactic(
        WaveConfig? wave,
        IReadOnlyList<Aircraft> aircraft,
        double lossRatio)
    {
        var assessment = AssessWave(wave, aircraft, lossRatio);
        if (assessment.PanicState)
            return GroupTactic.TimeOnTarget;
        if (assessment.Tags.Contains("SEAD"))
            return GroupTactic.SEAD;
        if (assessment.Tags.Contains("DECOY"))
            return GroupTactic.FeintAndStrike;
        if (assessment.Tags.Contains("JAMMER") && assessment.EscortRequired)
            return GroupTactic.ECMEscortPackage;
        if (assessment.Tags.Contains("LOW_ALT"))
            return GroupTactic.TerrainMasking;
        return assessment.EscortRequired ? GroupTactic.PincerAttack : GroupTactic.StraightIngress;
    }

    public static string BuildAiGuidance(
        ScenarioDefinition? scenario,
        SimulationSnapshot snapshot,
        EnemyCommanderProfile profile,
        EngagementRulesProfile? rulesProfile = null)
    {
        rulesProfile ??= EngagementRulesProfile.MainGame;
        var sb = new StringBuilder();
        sb.AppendLine(rulesProfile.BuildSummary());
        if (scenario != null)
        {
            foreach (var wave in scenario.EnemyForces.Waves.Take(4))
            {
                var groupAircraft = snapshot.HostileAircraft
                    .Where(a => string.Equals(a.GroupId, wave.PackageName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                double lossRatio = groupAircraft.Count == 0 ? 0 : Math.Clamp(1 - (groupAircraft.Count / (double)ExpectedWaveCount(wave)), 0, 1);
                var assessment = AssessWave(wave, groupAircraft, lossRatio, rulesProfile);
                sb.AppendLine($"{wave.PackageName}: {string.Join(", ", assessment.Tags)}");
                sb.AppendLine($"- Formation: {assessment.FormationRule}");
                sb.AppendLine($"- Support: {assessment.SupportRule}");
                sb.AppendLine($"- Morale: {assessment.MoraleRule}");
            }
        }

        sb.AppendLine($"Commander losses tolerance: {profile.ToleranceForLosses:P0}. Retreat if losses exceed rules or morale breaks.");
        return sb.ToString().TrimEnd();
    }

    private static int ExpectedWaveCount(WaveConfig wave) =>
        Math.Max(1, wave.Aircraft.Sum(a => Math.Max(1, a.Count)));

    private static List<string> BuildTags(WaveConfig? wave, IReadOnlyList<Aircraft> aircraft)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string role = wave?.PackageRole?.ToUpperInvariant() ?? string.Empty;
        string name = wave?.PackageName?.ToUpperInvariant() ?? string.Empty;

        if (role.Contains("STRIKE") || aircraft.Any(a => a.Role == AircraftRole.Striker))
            tags.Add("STRIKE");
        if (role.Contains("FIGHTER") || aircraft.Any(a => a.Role == AircraftRole.Fighter))
            tags.Add("ESCORT");
        if (role.Contains("JAM") || aircraft.Any(a => a.HasECM || a.Role == AircraftRole.ECMEscort))
            tags.Add("JAMMER");
        if (role.Contains("FEINT") || role.Contains("DECOY") || aircraft.Any(a => a.Role == AircraftRole.Decoy || a.Type == EntityType.Drone))
            tags.Add("DECOY");
        if (role.Contains("SEAD") || aircraft.Any(a => a.Role == AircraftRole.SEAD || a.HasARMCapability))
            tags.Add("SEAD");
        if (role.Contains("LOW") || role.Contains("TERRAIN") || name.Contains("SHADE"))
            tags.Add("LOW_ALT");
        if (aircraft.Any(a => a.Role == AircraftRole.Recon))
            tags.Add("RECON");
        if (tags.Count == 0)
            tags.Add("GENERAL");
        return tags.ToList();
    }
}
