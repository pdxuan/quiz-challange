using System.Collections.Concurrent;
using Quiz.Server.Domain;
using Quiz.Server.Runtime;

namespace Quiz.Server.Tests.TestHost;

/// <summary>Stands in for the SignalR transport (S4): records what the actor publishes after each commit.</summary>
public sealed class RecordingSessionEvents : ISessionEvents
{
    private readonly ConcurrentQueue<(string QuizId, SessionEvent Event)> _published = new();

    public void Publish(Session session, IReadOnlyList<SessionEvent> events)
    {
        foreach (var e in events)
        {
            _published.Enqueue((session.QuizId, e));
        }
    }

    public IReadOnlyList<SessionEvent> For(string quizId) =>
        _published.Where(p => p.QuizId == quizId).Select(p => p.Event).ToList();
}
