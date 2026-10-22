namespace Quiz.Server.Domain;

/// <param name="TotalPoints">The points shown on the board (may be lower than <see cref="Player.TotalPoints"/> while a question is open).</param>
public sealed record RankedPlayer(int Rank, Player Player, int TotalPoints);

public static class Ranking
{
    public static IReadOnlyList<RankedPlayer> Rank(IEnumerable<Player> players) =>
        RankStandings(players.Select(p => (p, p.TotalPoints, p.ReachedAt)));

    /// <summary>
    /// Competition ranking on points (1, 1, 3). Inside a tie the earliest time the points were
    /// reached comes first, then the display name.
    /// </summary>
    public static IReadOnlyList<RankedPlayer> RankStandings(IEnumerable<(Player Player, int Points, DateTimeOffset ReachedAt)> standings)
    {
        var ordered = standings
            .OrderByDescending(s => s.Points)
            .ThenBy(s => s.ReachedAt)
            .ThenBy(s => s.Player.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ranked = new List<RankedPlayer>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var rank = i > 0 && ordered[i].Points == ordered[i - 1].Points ? ranked[i - 1].Rank : i + 1;
            ranked.Add(new RankedPlayer(rank, ordered[i].Player, ordered[i].Points));
        }
        return ranked;
    }
}
