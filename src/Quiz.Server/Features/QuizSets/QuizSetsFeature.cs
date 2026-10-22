// AI-assisted: Claude Code (Claude Opus 5.5), slice S6; reviewed by a reviewer agent and a human; verified by Features/QuizSets/QuizSetsApiTests (docs/ai-collab/s6.md).
using Microsoft.EntityFrameworkCore;
using Quiz.Server.Data;

namespace Quiz.Server.Features.QuizSets;

/// <summary>One stored quiz set as the home page lists it: no questions, options or answers.</summary>
public sealed record QuizSetSummary(string Id, string Title, int QuestionCount, int TimeLimitSec);

/// <summary>Lists the quiz sets without their answers.</summary>
public static class QuizSetsFeature
{
    public static IServiceCollection AddQuizSetsFeature(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapQuizSetsFeature(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/quiz-sets", List)
            .WithTags("QuizSets")
            .WithName("ListQuizSets")
            .WithSummary("Lists the stored quiz sets (title, question count, time limit); never their answers.")
            .Produces<IReadOnlyList<QuizSetSummary>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> List(IDbContextFactory<QuizDbContext> dbFactory, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var sets = await db.QuizSets.AsNoTracking()
            .OrderBy(s => s.Title).ThenBy(s => s.Id)
            .Select(s => new QuizSetSummary(s.Id, s.Title, s.Questions.Count, s.TimeLimitSec))
            .ToListAsync(cancellationToken);
        return Results.Ok(sets);
    }
}
