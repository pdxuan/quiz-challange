using Quiz.Server.Domain;
using static Quiz.Server.Tests.Domain.DomainBuilders;

namespace Quiz.Server.Tests.Domain;

public class RankingTests
{
    private static Player P(string name, int points, int reachedAtSec) =>
        new("ABC234", Guid.NewGuid(), name, T0) { TotalPoints = points, ReachedAt = T0.AddSeconds(reachedAtSec) };

    [Fact]
    public void Rank_OrdersByPointsDescending()
    {
        var ranked = Ranking.Rank([P("low", 500, 1), P("high", 1800, 1), P("mid", 900, 1)]);

        Assert.Equal(["high", "mid", "low"], ranked.Select(r => r.Player.DisplayName));
        Assert.Equal([1, 2, 3], ranked.Select(r => r.Rank));
    }

    [Fact]
    public void Rank_EqualPoints_ShareRankAndNextRankSkips()
    {
        var ranked = Ranking.Rank([P("a", 1000, 3), P("b", 1000, 1), P("c", 1500, 1), P("d", 400, 1)]);

        Assert.Equal([("c", 1), ("b", 2), ("a", 2), ("d", 4)], ranked.Select(r => (r.Player.DisplayName, r.Rank)));
    }

    [Fact]
    public void Rank_EqualPoints_OrdersByEarliestReachedAtThenName()
    {
        var ranked = Ranking.Rank([P("zed", 700, 5), P("bob", 700, 2), P("amy", 700, 5)]);

        Assert.Equal(["bob", "amy", "zed"], ranked.Select(r => r.Player.DisplayName));
        Assert.All(ranked, r => Assert.Equal(1, r.Rank));
    }

    [Fact]
    public void Rank_Empty_ReturnsEmpty()
    {
        Assert.Empty(Ranking.Rank([]));
    }
}
