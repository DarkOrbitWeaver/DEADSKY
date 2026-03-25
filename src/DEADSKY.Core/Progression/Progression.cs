namespace DEADSKY.Core.Progression;

public enum PlayerRank
{
    Lieutenant,
    Captain,
    Major,
    Colonel
}

public sealed class PlayerProfile
{
    public int MissionsCompleted { get; set; }
    public int TotalKills { get; set; }
    public PlayerRank Rank { get; private set; } = PlayerRank.Lieutenant;

    public void EvaluateRankPromotion()
    {
        Rank = MissionsCompleted switch
        {
            >= 12 => PlayerRank.Colonel,
            >= 7 => PlayerRank.Major,
            >= 3 => PlayerRank.Captain,
            _ => PlayerRank.Lieutenant
        };
    }
}
