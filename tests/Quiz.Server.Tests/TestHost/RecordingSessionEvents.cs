using System.Collections.Concurrent;
using Quiz.Server.Domain;
using Quiz.Server.Runtime;

namespace Quiz.Server.Tests.TestHost;

/// <summary>Records what the actor publishes after each commit, then passes it on to the real transport (SignalR).</summary>
public sealed class RecordingSessionEvents : ISessionEvents
{
    private readonly ConcurrentQueue<(string QuizId, SessionEvent Event)> _published = new();

    public ISessionEvents? Inner { get; set; }

    public void Publish(Session session, IReadOnlyList<SessionEvent> events)
    {
        foreach (var e in events)
        {
            _published.Enqueue((session.QuizId, e));
        }
        Inner?.Publish(session, events);
    }

    public IReadOnlyList<SessionEvent> For(string quizId) =>
        _published.Where(p => p.QuizId == quizId).Select(p => p.Event).ToList();
}
