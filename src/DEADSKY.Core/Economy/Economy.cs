namespace DEADSKY.Core.Economy;

public sealed class BudgetSystem
{
    public int Balance { get; private set; } = 25000;

    public void Earn(int amount, string reason)
    {
        Balance += Math.Max(0, amount);
        Console.WriteLine($"[BUDGET] +{amount} OB ({reason}) => {Balance}");
    }

    public bool CanAfford(int amount) => Balance >= Math.Max(0, amount);

    public bool Spend(int amount, string reason)
    {
        int cost = Math.Max(0, amount);
        if (Balance < cost)
            return false;

        Balance -= cost;
        Console.WriteLine($"[BUDGET] -{cost} OB ({reason}) => {Balance}");
        return true;
    }

    public void SetBalance(int balance)
    {
        Balance = Math.Max(0, balance);
    }
}

public sealed class MissionReward
{
    public int BaseReward { get; init; }
    public int EfficiencyBonus { get; init; }
    public int ObjectiveModifier { get; init; }
    public int TotalReward => BaseReward + EfficiencyBonus;
}

public static class MissionRewardCalculator
{
    public static MissionReward Calculate(
        int baseReward,
        int kills,
        int missilesFired,
        bool crewHealthy,
        bool preventedBreakthrough,
        double hitRate,
        int missionNumber,
        int objectiveModifier = 0)
    {
        int bonus = kills * 500;
        bonus += (int)(hitRate * 2000);
        if (crewHealthy) bonus += 750;
        if (preventedBreakthrough) bonus += 1000;
        bonus += missionNumber * 100;
        if (missilesFired <= kills * 2) bonus += 500;
        bonus += objectiveModifier;

        return new MissionReward
        {
            BaseReward = baseReward,
            EfficiencyBonus = bonus,
            ObjectiveModifier = objectiveModifier
        };
    }
}
