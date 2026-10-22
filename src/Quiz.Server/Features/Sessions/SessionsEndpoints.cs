using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Quiz.Server.Data;
using Quiz.Server.Domain;
using Quiz.Server.Runtime;

namespace Quiz.Server.Features.Sessions;

public sealed record CreateSessionRequest(string? QuizSetId);

public sealed record CreatedSessionResponse(string QuizId);

public sealed record StartedSessionResponse(string State);

public sealed record LeaderboardEntry(int Rank, string Username, int TotalPoints, bool Online);

public sealed record LeaderboardResponse(string State, IReadOnlyList<LeaderboardEntry> Entries);

internal static class SessionsEndpoints
{
    public static async Task<IResult> Create(CreateSessionRequest request, SessionRegistry registry, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(request.QuizSetId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["quizSetId"] = ["Quiz set is required."] });
        }

        try
        {
            var quizId = await registry.CreateAsync(request.QuizSetId.Trim(), UserId(user));
            return quizId is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Quiz set not found")
                : Results.Created($"/api/sessions/{quizId}/leaderboard", new CreatedSessionResponse(quizId));
        }
        catch (SessionException ex)
        {
            return Problem(ex.Error);
        }
    }

    public static async Task<IResult> Start(string quizId, SessionRegistry registry, ClaimsPrincipal user)
    {
        var organizer = UserId(user);
        try
        {
            var state = await registry.AskAsync(quizId, (session, call) =>
            {
                session.Start(organizer, call.Now);
                return session.State;
            });
            return Results.Ok(new StartedSessionResponse(state.ToString()));
        }
        catch (SessionException ex)
        {
            return Problem(ex.Error);
        }
    }

    /// <summary>A read: straight from SQLite, not through the actor.</summary>
    public static async Task<IResult> Leaderboard(string quizId, IDbContextFactory<QuizDbContext> dbFactory, CancellationToken cancellationToken)
    {
        var id = QuizIdGenerator.Normalize(quizId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions.AsNoTracking()
            .Include(s => s.QuizSet).ThenInclude(q => q.Questions)
            .Include(s => s.Players)
            .Include(s => s.Answers)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.QuizId == id, cancellationToken);
        if (session is null)
        {
            return Problem(SessionError.QuizNotFound);
        }

        var entries = session.Leaderboard()
            .Select(r => new LeaderboardEntry(r.Rank, r.Player.DisplayName, r.TotalPoints, r.Player.Online))
            .ToList();
        return Results.Ok(new LeaderboardResponse(session.State.ToString(), entries));
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>The error code is the title, so clients can switch on it.</summary>
    private static IResult Problem(SessionError error) => Results.Problem(title: error.ToString(), statusCode: error switch
    {
        SessionError.QuizNotFound => StatusCodes.Status404NotFound,
        SessionError.NotOrganizer => StatusCodes.Status403Forbidden,
        SessionError.Invalid => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status409Conflict,
    });
}
