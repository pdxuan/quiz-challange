using Quiz.Server.Domain;

namespace Quiz.Server.Runtime;

/// <summary>Session settings from the "Sessions" config section.</summary>
public sealed class SessionOptions
{
    public const string Section = "Sessions";

    public int Capacity { get; set; } = 20;

    public double RevealSeconds { get; set; } = 5;

    /// <summary>How often each live session's clock checks deadlines.</summary>
    public int TickMilliseconds { get; set; } = 100;

    public SessionRules ToRules() => new(Capacity, TimeSpan.FromSeconds(RevealSeconds));
}
