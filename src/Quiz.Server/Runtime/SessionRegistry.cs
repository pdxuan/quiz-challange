// AI-assisted: Claude Code (Claude Opus 5.5), slice S3; reviewed by a reviewer agent and a human; verified by Runtime/SessionActorTests (docs/ai-collab/s3.md).
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quiz.Server.Data;
using Quiz.Server.Domain;
using Quiz.Server.Observability;

namespace Quiz.Server.Runtime;

/// <summary>Finds (or starts) the one actor of a quiz ID. Every write to a session goes through here.</summary>
public sealed class SessionRegistry(
    IDbContextFactory<QuizDbContext> dbFactory,
    ISessionEvents events,
    TimeProvider time,
    IOptions<SessionOptions> options,
    ILogger<SessionActor> actorLogger,
    QuizMetrics metrics) : IAsyncDisposable
{
    private const int MaxIdAttempts = 10;

    private readonly ConcurrentDictionary<string, Lazy<SessionActor>> _actors = new();

    /// <summary>Applies <paramref name="apply"/> to the session in its actor and returns the result after the save.</summary>
    /// <exception cref="SessionException">A rule failed, or the quiz does not exist (<see cref="SessionError.QuizNotFound"/>).</exception>
    /// <exception cref="SessionActorException">Something unexpected failed; nothing was saved.</exception>
    public async Task<T> AskAsync<T>(string quizId, Func<Session, SessionCall, T> apply)
    {
        var id = QuizIdGenerator.Normalize(quizId);
        if (!QuizIdGenerator.IsValid(id))
        {
            throw new SessionException(SessionError.QuizNotFound);
        }

        while (true)
        {
            var actor = _actors.GetOrAdd(id, key => new Lazy<SessionActor>(() => StartActor(key))).Value;
            if (actor.TryAsk(apply, out var reply))
            {
                return await reply;
            }
            // It is stopping: let it finish what it has queued before a fresh actor touches the session.
            await actor.Completion;
            Forget(actor);
        }
    }

    /// <summary>Creates a Lobby session with a new quiz ID, or null if the quiz set does not exist.</summary>
    public async Task<string?> CreateAsync(string quizSetId, Guid organizerUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var quizSet = await db.QuizSets.Include(s => s.Questions).ThenInclude(q => q.Options)
            .SingleOrDefaultAsync(s => s.Id == quizSetId);
        if (quizSet is null)
        {
            return null;
        }

        for (var attempt = 0; attempt < MaxIdAttempts; attempt++)
        {
            var quizId = QuizIdGenerator.New();
            if (await db.Sessions.AnyAsync(s => s.QuizId == quizId))
            {
                continue;
            }
            db.Sessions.Add(Session.Create(quizId, quizSet, organizerUserId, time.GetUtcNow()));
            await db.SaveChangesAsync();
            return quizId;
        }
        throw new InvalidOperationException("No free quiz ID was found.");
    }

    /// <summary>Actors that are running now (each keeps a clock ticking).</summary>
    public int LiveActorCount => _actors.Count;

    /// <summary>Waits until every live actor has handled what is queued (tests and shutdown).</summary>
    public Task FlushAllAsync() =>
        Task.WhenAll(_actors.Values.Where(a => a.IsValueCreated).Select(a => a.Value.FlushAsync()));

    public async ValueTask DisposeAsync()
    {
        foreach (var actor in _actors.Values.Where(a => a.IsValueCreated))
        {
            await actor.Value.DisposeAsync();
        }
    }

    private SessionActor StartActor(string quizId) =>
        new(quizId, dbFactory, events, time, options.Value, actorLogger, metrics, Forget);

    private void Forget(SessionActor actor)
    {
        if (_actors.TryGetValue(actor.QuizId, out var current) && current.IsValueCreated && current.Value == actor)
        {
            _actors.TryRemove(new KeyValuePair<string, Lazy<SessionActor>>(actor.QuizId, current));
        }
    }
}
