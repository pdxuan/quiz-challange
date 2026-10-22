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
