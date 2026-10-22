namespace Quiz.Server.Domain;

public sealed record RankedPlayer(int Rank, Player Player);

public static class Ranking
{
    /// <summary>
    /// Competition ranking on total points (1, 1, 3). Inside a tie the earliest
    /// <see cref="Player.ReachedAt"/> comes first, then the display name.
    /// </summary>
    public static IReadOnlyList<RankedPlayer> Rank(IEnumerable<Player> players)
    {
        var ordered = players
            .OrderByDescending(p => p.TotalPoints)
            .ThenBy(p => p.ReachedAt)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ranked = new List<RankedPlayer>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var rank = i > 0 && ordered[i].TotalPoints == ordered[i - 1].TotalPoints ? ranked[i - 1].Rank : i + 1;
            ranked.Add(new RankedPlayer(rank, ordered[i]));
        }
        return ranked;
    }
}
