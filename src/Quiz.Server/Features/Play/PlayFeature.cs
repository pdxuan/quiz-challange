namespace Quiz.Server.Features.Play;

/// <summary>The real-time quiz hub at /hubs/quiz (filled in S4).</summary>
public static class PlayFeature
{
    public static IServiceCollection AddPlayFeature(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapPlayFeature(this IEndpointRouteBuilder app) => app;
}
