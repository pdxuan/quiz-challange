// AI-assisted: Claude Code (Claude Opus 5.5), slice S1; reviewed by a reviewer agent and a human; verified by ScoringTests, 3 mutants (docs/ai-collab/s1.md).
namespace Quiz.Server.Domain;

public static class Scoring
{
    /// <summary>
    /// Correct = 500 + round(500 * remaining / limit), so 500-1000; wrong = 0. Remaining is
    /// clamped to [0, limit] and the bonus rounds half away from zero (not banker's rounding).
    /// </summary>
    public static int Points(bool correct, TimeSpan remaining, TimeSpan limit)
    {
        if (!correct)
        {
            return 0;
        }

        var clamped = Math.Clamp(remaining.TotalMilliseconds, 0, limit.TotalMilliseconds);
        return 500 + (int)Math.Round(500 * clamped / limit.TotalMilliseconds, MidpointRounding.AwayFromZero);
    }
}
