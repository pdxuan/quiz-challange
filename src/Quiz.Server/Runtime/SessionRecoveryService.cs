using Microsoft.EntityFrameworkCore;
using Quiz.Server.Data;
using Quiz.Server.Domain;

namespace Quiz.Server.Runtime;

/// <summary>
/// After a restart no connection survives: every unfinished session is resumed with all players
/// offline. The actor's clock then closes an overdue question at once, and an overdue reveal gets
/// a fresh one; players who rejoin during the reveal keep the quiz going (docs/TASK.md, restart
/// assumption).
/// </summary>
public sealed class SessionRecoveryService(IDbContextFactory<QuizDbContext> dbFactory, SessionRegistry registry, ILogger<SessionRecoveryService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var unfinished = await db.Sessions
            .Where(s => s.State != SessionState.Finished)
            .Select(s => s.QuizId)
            .ToListAsync(cancellationToken);

        foreach (var quizId in unfinished)
        {
            try
            {
                await registry.AskAsync(quizId, (session, call) => { session.Resume(call.Now, call.Rules); return 0; });
            }
            catch (Exception ex) when (ex is SessionException or SessionActorException)
            {
                // One unreadable session must not keep the server from starting.
                logger.LogWarning("Could not resume quiz {QuizId}; it is skipped", quizId);
            }
        }
        if (unfinished.Count > 0)
        {
            logger.LogInformation("Resumed {Count} unfinished sessions with every player offline", unfinished.Count);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
