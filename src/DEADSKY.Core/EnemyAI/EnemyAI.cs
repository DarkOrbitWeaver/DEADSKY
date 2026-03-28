using System.Text;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Logging;

namespace DEADSKY.Core.EnemyAI;

public enum GroupTactic
{
    StraightIngress,
    PincerAttack,
    FeintAndStrike,
    ECMEscortPackage,
    Saturation,
    PopUpAttack,
    TerrainMasking,
    DroneDecoy,
    SEAD,
    TimeOnTarget
}

public sealed class AircraftGroup
{
    public string GroupId { get; init; } = "";
    public List<string> AircraftIds { get; } = new();
    public GroupTactic Tactic { get; set; } = GroupTactic.StraightIngress;
}

public sealed class EnemyCommanderProfile
{
    public string Name { get; init; } = "";
    public string Callsign { get; init; } = "";
    public double Aggressiveness { get; init; }
    public double Caution { get; init; }
    public double Adaptability { get; init; }
    public double Creativity { get; init; }
    public double ToleranceForLosses { get; init; }

    public static EnemyCommanderProfile ForChapter(int chapter) => chapter switch
    {
        <= 3 => new EnemyCommanderProfile
        {
            Name = "Captain Yusuf Maric",
            Callsign = "RAVEN ACTUAL",
            Aggressiveness = 0.45,
            Caution = 0.7,
            Adaptability = 0.5,
            Creativity = 0.4,
            ToleranceForLosses = 0.25
        },
        <= 7 => new EnemyCommanderProfile
        {
            Name = "Major Dragan Kovac",
            Callsign = "RAVEN ACTUAL",
            Aggressiveness = 0.7,
            Caution = 0.4,
            Adaptability = 0.75,
            Creativity = 0.65,
            ToleranceForLosses = 0.45
        },
        _ => new EnemyCommanderProfile
        {
            Name = "Colonel Sera Volin",
            Callsign = "RAVEN ACTUAL",
            Aggressiveness = 0.82,
            Caution = 0.32,
            Adaptability = 0.88,
            Creativity = 0.8,
            ToleranceForLosses = 0.55
        }
    };

    public string BuildSystemPromptContext() =>
        $"Commander: {Name} ({Callsign})\n" +
        $"Aggressiveness: {Aggressiveness:F2}\n" +
        $"Caution: {Caution:F2}\n" +
        $"Adaptability: {Adaptability:F2}\n" +
        $"Creativity: {Creativity:F2}\n" +
        $"Tolerance for losses: {ToleranceForLosses:F2}";
}

public sealed class GroupTacticManager
{
    private readonly EntityManager _entities;
    private readonly Dictionary<string, AircraftGroup> _groups = new();

    public GroupTacticManager(EntityManager entities)
    {
        _entities = entities;
    }

    public AircraftGroup CreateGroup(string groupId, IEnumerable<string> aircraftIds, GroupTactic tactic)
    {
        var group = new AircraftGroup { GroupId = groupId, Tactic = tactic };
        group.AircraftIds.AddRange(aircraftIds.Distinct());
        _groups[groupId] = group;
        ApplyGroupTactic(group);
        return group;
    }

    public AircraftGroup? GetGroup(string groupId) =>
        _groups.TryGetValue(groupId, out var group) ? group : null;

    public void SetGroupTactic(string groupId, GroupTactic tactic)
    {
        if (!_groups.TryGetValue(groupId, out var group))
            return;

        group.Tactic = tactic;
        ApplyGroupTactic(group);
    }

    public string BuildContextForAI()
    {
        if (_groups.Count == 0)
            return "Enemy groups: none currently formed.";

        var sb = new StringBuilder();
        sb.AppendLine("Enemy groups in theatre:");
        foreach (var group in _groups.Values.OrderBy(g => g.GroupId))
            sb.AppendLine($"- {group.GroupId}: tactic={group.Tactic}, aircraft={group.AircraftIds.Count}");
        return sb.ToString().TrimEnd();
    }

    private void ApplyGroupTactic(AircraftGroup group)
    {
        foreach (var aircraftId in group.AircraftIds)
        {
            if (_entities.Get(aircraftId) is not Aircraft aircraft)
                continue;

            aircraft.GroupId = group.GroupId;
            var next = group.Tactic switch
            {
                GroupTactic.PincerAttack => AircraftBehavior.IngressAttack,
                GroupTactic.FeintAndStrike => aircraft.FormationSlot == 0 ? AircraftBehavior.Feint : AircraftBehavior.IngressAttack,
                GroupTactic.ECMEscortPackage => aircraft.HasECM ? AircraftBehavior.ECMStandoff : AircraftBehavior.IngressAttack,
                GroupTactic.Saturation => AircraftBehavior.IngressAttack,
                GroupTactic.PopUpAttack => AircraftBehavior.PopUpAttack,
                GroupTactic.TerrainMasking => AircraftBehavior.TerrainFollowing,
                GroupTactic.DroneDecoy => aircraft.Type == EntityType.Drone ? AircraftBehavior.Feint : AircraftBehavior.IngressAttack,
                GroupTactic.SEAD => AircraftBehavior.SEAD,
                GroupTactic.TimeOnTarget => AircraftBehavior.OrbitPatrol,
                _ => AircraftBehavior.IngressAttack
            };
            if (aircraft.CurrentBehavior != next)
                GameSessionLogger.Current?.OnBehaviorChanged(aircraft.Id, aircraft.Designation,
                    aircraft.CurrentBehavior.ToString(), next.ToString(),
                    $"ai_cmd tactic={group.Tactic} group={group.GroupId}");
            aircraft.CurrentBehavior = next;
        }
    }
}
