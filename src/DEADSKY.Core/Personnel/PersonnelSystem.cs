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

public sealed class CrewRoster
{
    public List<Soldier> Soldiers { get; } = new();
    public event Action<Soldier, string>? CrewEventOccurred;

    public static CrewRoster CreateDefaultCrew()
    {
        var roster = new CrewRoster();
        roster.Soldiers.AddRange(new[]
        {
            new Soldier { FullName = "Capt. Nadia El Khatib", Role = SoldierRole.Commander, Proficiency = 0.86 },
            new Soldier { FullName = "Sgt. Omar Saidi", Role = SoldierRole.RadarOperator, Proficiency = 0.78 },
            new Soldier { FullName = "Cpl. Ilham Rahal", Role = SoldierRole.FireControl, Proficiency = 0.74 },
            new Soldier { FullName = "Spc. Yassine Mourad", Role = SoldierRole.LauncherChief, Proficiency = 0.7 },
            new Soldier { FullName = "Spc. Lina Aouad", Role = SoldierRole.Signals, Proficiency = 0.72 }
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
}
