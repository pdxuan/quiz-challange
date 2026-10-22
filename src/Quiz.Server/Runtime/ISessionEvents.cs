using Quiz.Server.Domain;

namespace Quiz.Server.Runtime;

/// <summary>Where committed events go. S4 sends them over SignalR; until then nothing listens.</summary>
public interface ISessionEvents
{
    /// <summary>Called on the actor after the save committed. Must not block: hand the work off and return.</summary>
    void Publish(Session session, IReadOnlyList<SessionEvent> events);
}

public sealed class NullSessionEvents : ISessionEvents
{
    public void Publish(Session session, IReadOnlyList<SessionEvent> events)
    {
    }
}
