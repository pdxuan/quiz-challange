namespace Quiz.Server.Seed;

/// <summary>Seeds dev users and quiz sets into an empty database in Development (filled in S6).</summary>
public static class SeedFeature
{
    public static Task SeedDevDataAsync(this WebApplication app) => Task.CompletedTask;
}
