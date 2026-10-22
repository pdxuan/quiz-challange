namespace Quiz.Server.Domain;

public enum SessionState
{
    Lobby,
    Running,
    Finished,
}

public enum SessionPhase
{
    None,
    Question,
    Reveal,
}

/// <summary>Rule failures. The names are the error codes clients see (Contract in docs/TASK.md).</summary>
public enum SessionError
{
    Invalid,
    QuizNotFound,
    QuizFinished,
    SessionFull,
    IsOrganizer,
    NotOrganizer,
    NotInLobby,
    NoPlayers,
    NotJoined,
    NotStarted,
    NotCurrentQuestion,
    NotEligible,
    QuestionClosed,
    AlreadyAnswered,
}

public sealed class SessionException(SessionError error) : Exception(error.ToString())
{
    public SessionError Error { get; } = error;
}

/// <summary>Settings from configuration, passed in so they are not stored per session.</summary>
public sealed record SessionRules(int Capacity, TimeSpan RevealDuration);

public sealed record JoinOutcome(Player Player, bool Rejoined, string? ReplacedConnectionId, bool CanAnswerCurrent);

/// <summary>An accepted answer. It says nothing about correctness until the question closes.</summary>
public sealed record AnswerOutcome(string QuestionId, Guid SubmissionId);

/// <summary>What changed; the actor turns these into pushes after the save commits.</summary>
public abstract record SessionEvent;

public sealed record PlayerJoined(Guid UserId, string DisplayName, int PlayerCount) : SessionEvent;

public sealed record PlayerOnlineChanged(Guid UserId, string DisplayName, bool Online) : SessionEvent;

public sealed record QuestionStarted(string QuestionId, int Index, int Total, DateTimeOffset EndsAt) : SessionEvent;

public sealed record AnswersCountChanged(string QuestionId, int Answered, int Eligible) : SessionEvent;

public sealed record QuestionClosed(string QuestionId, string CorrectOptionId) : SessionEvent;

public sealed record QuizFinished : SessionEvent;
