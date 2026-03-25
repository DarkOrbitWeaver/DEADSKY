namespace DEADSKY.Core.Personnel;

public enum SoldierRole
{
    Commander,
    RadarOperator,
    FireControl,
    LauncherChief,
    Signals
}

public enum HealthStatus
{
    Healthy,
    Shaken,
    Wounded,
    KIA
}

public sealed class Soldier
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    public string FullName { get; init; } = "";
    public SoldierRole Role { get; init; }
    public double Morale { get; set; } = 0.75;
    public double Fear { get; set; } = 0.1;
    public double Proficiency { get; set; } = 0.6;
    public HealthStatus Health { get; set; } = HealthStatus.Healthy;
}

public sealed class CrewMemberState
{
    public string SoldierId { get; set; } = "";
    public string FullName { get; set; } = "";
    public SoldierRole Role { get; set; }
    public double Morale { get; set; } = 0.75;
    public double Fear { get; set; } = 0.1;
    public double Proficiency { get; set; } = 0.6;
    public HealthStatus Health { get; set; } = HealthStatus.Healthy;
}

public sealed class CrewRoster
{
    public List<Soldier> Soldiers { get; } = new();
    public event Action<Soldier, string>? CrewEventOccurred;

    public static CrewRoster CreateDefaultCrew()
    {
        var roster = new CrewRoster();
        roster.Soldiers.AddRange(new[]
        {
            new Soldier { Id = "CMD-001", FullName = "Capt. Nadia El Khatib", Role = SoldierRole.Commander, Proficiency = 0.86 },
            new Soldier { Id = "RAD-001", FullName = "Sgt. Omar Saidi", Role = SoldierRole.RadarOperator, Proficiency = 0.78 },
            new Soldier { Id = "FCR-001", FullName = "Cpl. Ilham Rahal", Role = SoldierRole.FireControl, Proficiency = 0.74 },
            new Soldier { Id = "LCH-001", FullName = "Spc. Yassine Mourad", Role = SoldierRole.LauncherChief, Proficiency = 0.7 },
            new Soldier { Id = "SIG-001", FullName = "Spc. Lina Aouad", Role = SoldierRole.Signals, Proficiency = 0.72 }
        });
        return roster;
    }

    public void Update(double deltaTime, bool underFire, bool recentKill, bool recentLoss)
    {
        foreach (var soldier in Soldiers)
        {
            if (soldier.Health == HealthStatus.KIA)
                continue;

            if (underFire)
            {
                soldier.Fear = Math.Clamp(soldier.Fear + deltaTime * 0.02, 0, 1);
                soldier.Morale = Math.Clamp(soldier.Morale - deltaTime * 0.005, 0, 1);
            }
            else
            {
                soldier.Fear = Math.Clamp(soldier.Fear - deltaTime * 0.015, 0, 1);
            }

            if (recentKill)
                soldier.Morale = Math.Clamp(soldier.Morale + 0.02, 0, 1);
            if (recentLoss)
                soldier.Morale = Math.Clamp(soldier.Morale - 0.03, 0, 1);
        }
    }

    public void ApplyMoraleBoost(double amount, string reason)
    {
        foreach (var soldier in Soldiers)
        {
            soldier.Morale = Math.Clamp(soldier.Morale + amount, 0, 1);
            CrewEventOccurred?.Invoke(soldier, $"morale_boost:{reason}");
        }
    }

    public void ApplyFearEvent(double amount, string reason)
    {
        foreach (var soldier in Soldiers)
        {
            soldier.Fear = Math.Clamp(soldier.Fear + amount, 0, 1);
            soldier.Morale = Math.Clamp(soldier.Morale - amount * 0.4, 0, 1);
            CrewEventOccurred?.Invoke(soldier, $"fear:{reason}");
        }
    }

    public IReadOnlyList<CrewMemberState> CaptureState() =>
        Soldiers.Select(soldier => new CrewMemberState
        {
            SoldierId = soldier.Id,
            FullName = soldier.FullName,
            Role = soldier.Role,
            Morale = soldier.Morale,
            Fear = soldier.Fear,
            Proficiency = soldier.Proficiency,
            Health = soldier.Health
        }).ToList();

    public void ApplyState(IEnumerable<CrewMemberState> states)
    {
        foreach (var state in states)
        {
            var soldier = Soldiers.FirstOrDefault(s => s.Id.Equals(state.SoldierId, StringComparison.OrdinalIgnoreCase))
                ?? Soldiers.FirstOrDefault(s => s.FullName.Equals(state.FullName, StringComparison.OrdinalIgnoreCase))
                ?? Soldiers.FirstOrDefault(s => s.Role == state.Role);

            if (soldier == null)
                continue;

            soldier.Morale = Math.Clamp(state.Morale, 0, 1);
            soldier.Fear = Math.Clamp(state.Fear, 0, 1);
            soldier.Proficiency = Math.Clamp(state.Proficiency, 0, 1);
            soldier.Health = state.Health;
        }
    }

    public void RecoverAfterMission(bool victory)
    {
        foreach (var soldier in Soldiers)
        {
            if (soldier.Health == HealthStatus.KIA)
                continue;

            double moraleRecovery = victory ? 0.04 : 0.01;
            double fearRecovery = victory ? 0.08 : 0.04;
            soldier.Morale = Math.Clamp(soldier.Morale + moraleRecovery, 0, 1);
            soldier.Fear = Math.Clamp(soldier.Fear - fearRecovery, 0, 1);
        }
    }

    public string BuildConditionSummary()
    {
        if (Soldiers.Count == 0)
            return "CREW: NO ASSIGNED PERSONNEL";

        double avgMorale = Soldiers.Average(s => s.Morale);
        double avgFear = Soldiers.Average(s => s.Fear);
        int wounded = Soldiers.Count(s => s.Health == HealthStatus.Wounded);
        int shaken = Soldiers.Count(s => s.Health == HealthStatus.Shaken);
        int kia = Soldiers.Count(s => s.Health == HealthStatus.KIA);

        return $"CREW STATE: MORALE {avgMorale:P0} | FEAR {avgFear:P0} | SHAKEN {shaken} | WOUNDED {wounded} | KIA {kia}";
    }
}
