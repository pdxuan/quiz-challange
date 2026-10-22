using Quiz.Server.Domain;

namespace Quiz.Server.Tests.Domain;

public class ScoringTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData(15_000, 1000)] // answered instantly: full bonus
    [InlineData(7_500, 750)]   // half the time left
    [InlineData(1, 500)]       // 1 ms left: 500 + round(0.033)
    [InlineData(0, 500)]       // at the deadline (never accepted, but the formula stays in range)
    public void Points_Correct_Is500PlusTimeBonus(int remainingMs, int expected)
    {
        Assert.Equal(expected, Scoring.Points(true, TimeSpan.FromMilliseconds(remainingMs), Limit));
    }

    [Theory]
    [InlineData(15, 501)] // 500 * 15 / 15000 = 0.5 -> away from zero = 1 (banker's rounding would give 0)
    [InlineData(75, 503)] // 2.5 -> 3 (banker's rounding would give 2)
    public void Points_MidpointBonus_RoundsAwayFromZero(int remainingMs, int expected)
    {
        Assert.Equal(expected, Scoring.Points(true, TimeSpan.FromMilliseconds(remainingMs), Limit));
    }

    [Fact]
    public void Points_Wrong_IsZero()
    {
        Assert.Equal(0, Scoring.Points(false, TimeSpan.FromSeconds(10), Limit));
    }

    [Theory]
    [InlineData(-5_000, 500)]  // remaining below zero is clamped to 0
    [InlineData(20_000, 1000)] // remaining above the limit is clamped to the limit
    public void Points_RemainingOutOfRange_IsClamped(int remainingMs, int expected)
    {
        Assert.Equal(expected, Scoring.Points(true, TimeSpan.FromMilliseconds(remainingMs), Limit));
    }
}
