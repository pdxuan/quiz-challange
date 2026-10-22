namespace Quiz.Server.Features.Sessions;

/// <summary>Create, start and the leaderboard of a quiz session.</summary>
public static class SessionsFeature
{
    public static IServiceCollection AddSessionsFeature(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapSessionsFeature(this IEndpointRouteBuilder app)
    {
        var sessions = app.MapGroup("/api/sessions").WithTags("Sessions");

        sessions.MapPost("/", SessionsEndpoints.Create)
            .WithName("CreateSession")
            .WithSummary("Opens a Lobby session of a quiz set; the caller is its organizer.")
            .Produces<CreatedSessionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        sessions.MapPost("/{quizId}/start", SessionsEndpoints.Start)
            .WithName("StartSession")
            .WithSummary("Starts the quiz (organizer only, at least one online player).")
            .Produces<StartedSessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        sessions.MapGet("/{quizId}/leaderboard", SessionsEndpoints.Leaderboard)
            .WithName("GetLeaderboard")
            .WithSummary("The full ranked list; points of an open question stay hidden until it closes.")
            .Produces<LeaderboardResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
