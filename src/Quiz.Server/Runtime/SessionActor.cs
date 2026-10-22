// AI-assisted: Claude Code (Claude Opus 5.5), slice S3; reviewed by a reviewer agent and a human; verified by Runtime/SessionActorTests, mutations (docs/ai-collab/s3.md).
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Quiz.Server.Data;
using Quiz.Server.Domain;
using Quiz.Server.Observability;

namespace Quiz.Server.Runtime;

/// <summary>
/// Serialises every change to one session: calls queue in a channel and one loop applies them,
/// one at a time, as load -> apply -> save (one transaction) -> publish -> reply. Nothing is
/// cached between calls; SQLite is the truth. A timer queues a <c>Tick</c> so deadlines and
/// reveals advance. The actor stops when the session is not running (a Lobby has no deadlines,
/// a finished quiz has none left), does not exist, or failed <see cref="MaxConsecutiveFailures"/>
/// times in a row; the registry starts a fresh one on the next call. It leaves the registry only
/// after its queue has drained, so two actors never write the same session at once.
/// </summary>
public sealed class SessionActor : IAsyncDisposable
{
    public const int MaxConsecutiveFailures = 3;

    private readonly Channel<Func<Task>> _mailbox = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly IDbContextFactory<QuizDbContext> _dbFactory;
    private readonly ISessionEvents _events;
    private readonly TimeProvider _time;
    private readonly SessionRules _rules;
    private readonly ILogger<SessionActor> _logger;
    private readonly QuizMetrics _metrics;
    private readonly Action<SessionActor> _onStopped;
    private readonly ITimer _timer;
    private readonly Task _loop;
    private int _tickQueued;
    private int _stopped;
    private int _consecutiveFailures; // only touched on the loop

    public SessionActor(string quizId, IDbContextFactory<QuizDbContext> dbFactory, ISessionEvents events, TimeProvider time,
        SessionOptions options, ILogger<SessionActor> logger, QuizMetrics metrics, Action<SessionActor> onStopped)
    {
        QuizId = quizId;
        _dbFactory = dbFactory;
        _events = events;
        _time = time;
        _rules = options.ToRules();
        _logger = logger;
        _metrics = metrics;
        _onStopped = onStopped;
        _metrics.LiveSessions.Add(1);
        _loop = Task.Run(RunAsync);
        var interval = TimeSpan.FromMilliseconds(options.TickMilliseconds);
        _timer = time.CreateTimer(_ => QueueTick(), null, interval, interval);
    }

    public string QuizId { get; }

    /// <summary>Completes when the actor has stopped and its queue has drained.</summary>
    public Task Completion => _loop;

    /// <summary>Queues a call. False once the actor has stopped; the caller then asks a fresh actor.</summary>
    public bool TryAsk<T>(Func<Session, SessionCall, T> apply, out Task<T> reply)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        reply = completion.Task;
        return _mailbox.Writer.TryWrite(async () =>
        {
            try
            {
                completion.SetResult(await HandleAsync(apply));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
    }

    /// <summary>Completes once everything queued before it has been handled (or, once stopped, when the queue has drained).</summary>
    public Task FlushAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _mailbox.Writer.TryWrite(() => { done.SetResult(); return Task.CompletedTask; }) ? done.Task : _loop;
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        await _loop;
    }

    private async Task RunAsync()
    {
        await foreach (var work in _mailbox.Reader.ReadAllAsync())
        {
            await work(); // each work item handles its own errors, so one failure never stops the loop
        }
        _onStopped(this); // only now: calls queued before Stop have all run
    }

    private async Task<T> HandleAsync<T>(Func<Session, SessionCall, T> apply)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["quizId"] = QuizId });
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var session = await db.Sessions
                .Include(s => s.QuizSet).ThenInclude(q => q.Questions).ThenInclude(q => q.Options)
                .Include(s => s.Players)
                .Include(s => s.Answers)
                .AsSplitQuery()
                .SingleOrDefaultAsync(s => s.QuizId == QuizId);
            if (session is null)
            {
                Stop();
                throw new SessionException(SessionError.QuizNotFound);
            }

            var result = apply(session, new SessionCall(_time.GetUtcNow(), _rules));
            await db.SaveChangesAsync(); // a rule failure above throws first, so nothing is saved

            _consecutiveFailures = 0;
            Publish(session);
            if (session.State != SessionState.Running)
            {
                Stop();
            }
            return result;
        }
        catch (SessionException)
        {
            _consecutiveFailures = 0;
            throw; // a rule said no: expected, the caller maps the code
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session call failed for quiz {QuizId}; nothing was saved", QuizId);
            if (++_consecutiveFailures >= MaxConsecutiveFailures)
            {
                Stop(); // e.g. an unreadable row: stop ticking instead of logging the same error forever
            }
            throw new SessionActorException();
        }
        finally
        {
            _metrics.MessageDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <summary>Hands committed events to the transport without awaiting it, so a slow client never holds up the session.</summary>
    private void Publish(Session session)
    {
        if (session.Events.Count == 0)
        {
            return;
        }
        try
        {
            _events.Publish(session, session.Events.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Publishing events failed for quiz {QuizId}; the change is saved", QuizId);
        }
    }

    private void QueueTick()
    {
        if (Interlocked.Exchange(ref _tickQueued, 1) == 1)
        {
            return; // one tick is already waiting; it will see the latest time
        }
        var queued = _mailbox.Writer.TryWrite(async () =>
        {
            Volatile.Write(ref _tickQueued, 0);
            try
            {
                await HandleAsync((session, call) => { session.Tick(call.Now, call.Rules); return 0; });
            }
            catch (Exception)
            {
                // Already logged in HandleAsync; the next tick tries again.
            }
        });
        if (!queued)
        {
            Volatile.Write(ref _tickQueued, 0);
        }
    }

    private void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }
        _timer.Dispose();
        _mailbox.Writer.TryComplete(); // calls already queued still run; the loop then reports the stop
        _metrics.LiveSessions.Add(-1);
    }
}
