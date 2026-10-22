namespace Quiz.Server.Features.Sessions;

/// <summary>Create, start and the leaderboard of a quiz session (filled in S3).</summary>
public static class SessionsFeature
{
    public static IServiceCollection AddSessionsFeature(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapSessionsFeature(this IEndpointRouteBuilder app) => app;
}
