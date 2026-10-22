using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Quiz.Server.Domain;
using Quiz.Server.Observability;
using Quiz.Server.Runtime;

namespace Quiz.Server.Features.Play;

/// <summary>
/// Turns committed session events into SignalR messages. <see cref="Publish"/> runs on the actor:
/// it only builds the payloads from the session (no I/O) and queues the sends. Each quiz has its
/// own outbox, sent in order, so a slow client never holds up its session's actor nor another
/// session's pushes. Each player gets their own QuestionClosed/QuizFinished; the organizer view
/// gets the full board. AnswersCount is merged to at most one push per quiz per
/// <see cref="AnswersCountInterval"/>.
/// </summary>
public sealed class SignalRSessionEvents(IHubContext<QuizHub> hub, TimeProvider time, QuizMetrics metrics, ILogger<SignalRSessionEvents> logger)
    : ISessionEvents
{
    public static readonly TimeSpan AnswersCountInterval = TimeSpan.FromMilliseconds(200);

    private const int TopCount = 10;

    private readonly IHubContext<QuizHub> _hub = hub;
    private readonly TimeProvider _time = time;
    private readonly ConcurrentDictionary<string, Outbox> _outboxes = new();
    private readonly ConcurrentDictionary<string, AnswersCountThrottle> _throttles = new();

    public void Publish(Session session, IReadOnlyList<SessionEvent> events)
    {
        var quizId = session.QuizId;
        var group = _hub.Clients.Group(QuizGroups.Session(quizId));
        foreach (var e in events)
        {
            switch (e)
            {
                case PlayerJoined joined:
                    Queue(quizId, () => group.SendAsync(QuizEvents.PlayerJoined, new PlayerJoinedEvent(joined.DisplayName, joined.PlayerCount)));
                    break;
                case PlayerOnlineChanged changed:
                    Queue(quizId, () => group.SendAsync(QuizEvents.PlayerOnlineChanged, new PlayerOnlineChangedEvent(changed.DisplayName, changed.Online)));
                    break;
                case QuestionStarted started:
                    var view = Views.Question(session, started.QuestionId, _time.GetUtcNow());
                    Queue(quizId, () => group.SendAsync(QuizEvents.QuestionStarted, view));
                    break;
                case AnswersCountChanged count:
                    Throttle(quizId).Offer(new AnswersCountEvent(count.QuestionId, count.Answered, count.Eligible));
                    break;
                case QuestionClosed closed:
                    Throttle(quizId).Drop(); // a count for a closed question is stale
                    QueueClosed(session, closed);
                    break;
                case QuizFinished:
                    QueueFinished(session);
                    break;
            }
        }
    }

    private void Queue(string quizId, Func<Task> send) =>
        _outboxes.GetOrAdd(quizId, _ => new Outbox(logger)).Post(send);

    private void QueueClosed(Session session, QuestionClosed closed)
    {
        var started = Stopwatch.GetTimestamp();
        var ranked = session.Leaderboard();
        var top = Views.Board(ranked, TopCount);
        var perPlayer = session.Players
            .Where(p => p.Online && p.ConnectionId is not null)
            .Select(p =>
            {
                var answer = session.Answers.FirstOrDefault(a => a.UserId == p.UserId && a.QuestionId == closed.QuestionId);
                var you = Views.You(ranked, p.UserId);
                return (p.ConnectionId!, new PlayerQuestionClosed(closed.QuestionId, closed.CorrectOptionId, answer?.OptionId,
                    answer?.OptionId == closed.CorrectOptionId, answer?.Points ?? 0, you.TotalPoints, you.Rank, top));
            })
            .ToList();
        var organizer = new OrganizerQuestionClosed(closed.QuestionId, closed.CorrectOptionId, Views.Board(ranked));
        var quizId = session.QuizId;

        Queue(quizId, async () =>
        {
            foreach (var (connectionId, payload) in perPlayer)
            {
                await _hub.Clients.Client(connectionId).SendAsync(QuizEvents.QuestionClosed, payload);
            }
            await _hub.Clients.Group(QuizGroups.Organizer(quizId)).SendAsync(QuizEvents.QuestionClosed, organizer);
            metrics.ClosePushDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        });
    }

    private void QueueFinished(Session session)
    {
        var ranked = session.Leaderboard();
        var top = Views.Board(ranked, TopCount);
        var perPlayer = session.Players
            .Where(p => p.Online && p.ConnectionId is not null)
            .Select(p =>
            {
                var you = Views.You(ranked, p.UserId);
                return (p.ConnectionId!, new PlayerQuizFinished(you.TotalPoints, you.Rank, top));
            })
            .ToList();
        var organizer = new OrganizerQuizFinished(Views.Board(ranked));
        var quizId = session.QuizId;

        Queue(quizId, async () =>
        {
            foreach (var (connectionId, payload) in perPlayer)
            {
                await _hub.Clients.Client(connectionId).SendAsync(QuizEvents.QuizFinished, payload);
            }
            await _hub.Clients.Group(QuizGroups.Organizer(quizId)).SendAsync(QuizEvents.QuizFinished, organizer);
        });
    }

    /// <summary>
    /// One quiz's sends, in order. A drain runs only while there is work, so a finished quiz
    /// leaves no loop behind; at most one drain runs at a time.
    /// </summary>
    private sealed class Outbox(ILogger logger)
    {
        private readonly ConcurrentQueue<Func<Task>> _sends = new();
        private int _draining;

        public void Post(Func<Task> send)
        {
            _sends.Enqueue(send);
            if (Interlocked.CompareExchange(ref _draining, 1, 0) == 0)
            {
                _ = Task.Run(DrainAsync);
            }
        }

        private async Task DrainAsync()
        {
            while (true)
            {
                while (_sends.TryDequeue(out var send))
                {
                    try
                    {
                        await send();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "A push to clients failed");
                    }
                }
                Volatile.Write(ref _draining, 0);
                // A send posted after the last dequeue but before the reset would be stranded: take it back.
                if (_sends.IsEmpty || Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
                {
                    return;
                }
            }
        }
    }

    private AnswersCountThrottle Throttle(string quizId) =>
        _throttles.GetOrAdd(quizId, id => new AnswersCountThrottle(this, id));

    /// <summary>Sends the first count at once, then at most one per interval with the latest value.</summary>
    private sealed class AnswersCountThrottle(SignalRSessionEvents owner, string quizId)
    {
        private readonly Lock _gate = new();
        private DateTimeOffset _lastSent = DateTimeOffset.MinValue;
        private AnswersCountEvent? _pending;
        private ITimer? _timer;

        public void Offer(AnswersCountEvent count)
        {
            lock (_gate)
            {
                var now = owner._time.GetUtcNow();
                if (_timer is null && now - _lastSent >= AnswersCountInterval)
                {
                    Send(count, now);
                    return;
                }
                _pending = count; // merged: only the latest value is sent
                _timer ??= owner._time.CreateTimer(_ => Flush(), null, _lastSent + AnswersCountInterval - now, Timeout.InfiniteTimeSpan);
            }
        }

        public void Drop()
        {
            lock (_gate)
            {
                _pending = null;
                _timer?.Dispose();
                _timer = null;
            }
        }

        private void Flush()
        {
            lock (_gate)
            {
                _timer?.Dispose();
                _timer = null;
                if (_pending is { } count)
                {
                    _pending = null;
                    Send(count, owner._time.GetUtcNow());
                }
            }
        }

        private void Send(AnswersCountEvent count, DateTimeOffset now)
        {
            _lastSent = now;
            owner.Queue(quizId, () => owner._hub.Clients.Group(QuizGroups.Session(quizId)).SendAsync(QuizEvents.AnswersCount, count));
        }
    }
}
