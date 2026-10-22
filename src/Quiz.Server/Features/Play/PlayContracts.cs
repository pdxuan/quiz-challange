using Quiz.Server.Domain;

namespace Quiz.Server.Features.Play;

// The hub contract (docs/TASK.md, Contract): replies, events and how they are built from a session.
// The correct option appears only in QuestionClosed payloads, never in a question view.

public sealed record OptionView(string OptionId, string Text);

public sealed record QuestionView(string QuestionId, int Index, int Total, string Prompt, IReadOnlyList<OptionView> Options, DateTimeOffset EndsAt, DateTimeOffset ServerNow);

public sealed record BoardEntry(int Rank, string Username, int TotalPoints, bool Online);

public sealed record YouView(int TotalPoints, int Rank);

public sealed record JoinResult(string Username, bool IsOrganizer, string State, int PlayerCount, IReadOnlyList<PlayerView> Players, QuestionView? CurrentQuestion, bool CanAnswerCurrent, YouView You, IReadOnlyList<BoardEntry> Top10);

public sealed record PlayerView(string Username, bool Online);

public sealed record AnswersCountView(int Answered, int Eligible);

public sealed record WatchSnapshot(string State, IReadOnlyList<PlayerView> Players, QuestionView? CurrentQuestion, AnswersCountView? AnswersCount, IReadOnlyList<BoardEntry> Leaderboard);

public sealed record AnswerAccepted(string QuestionId, Guid SubmissionId);

public sealed record PlayerJoinedEvent(string Username, int PlayerCount);

public sealed record PlayerOnlineChangedEvent(string Username, bool Online);

public sealed record AnswersCountEvent(string QuestionId, int Answered, int Eligible);

public sealed record PlayerQuestionClosed(string QuestionId, string CorrectOptionId, string? YourOptionId, bool Correct, int Points, int TotalPoints, int Rank, IReadOnlyList<BoardEntry> Top10);

public sealed record OrganizerQuestionClosed(string QuestionId, string CorrectOptionId, IReadOnlyList<BoardEntry> Leaderboard);

public sealed record PlayerQuizFinished(int TotalPoints, int Rank, IReadOnlyList<BoardEntry> Top10);

public sealed record OrganizerQuizFinished(IReadOnlyList<BoardEntry> Leaderboard);

/// <summary>Client method names (server -> client events).</summary>
public static class QuizEvents
{
    public const string PlayerJoined = nameof(PlayerJoined);
    public const string PlayerOnlineChanged = nameof(PlayerOnlineChanged);
    public const string QuestionStarted = nameof(QuestionStarted);
    public const string AnswersCount = nameof(AnswersCount);
    public const string QuestionClosed = nameof(QuestionClosed);
    public const string QuizFinished = nameof(QuizFinished);
}

public static class QuizGroups
{
    /// <summary>Everyone in the session (players and the organizer view).</summary>
    public static string Session(string quizId) => $"quiz:{quizId}";

    /// <summary>The organizer view, which gets the full board.</summary>
    public static string Organizer(string quizId) => $"quiz:{quizId}:organizer";
}

internal static class Views
{
    public static QuestionView Question(Session session, string questionId, DateTimeOffset serverNow)
    {
        var ordered = session.QuizSet.Ordered;
        var question = ordered.Single(q => q.Id == questionId);
        return new QuestionView(question.Id, ordered.ToList().IndexOf(question), ordered.Count, question.Prompt,
            question.Options.OrderBy(o => o.Position).Select(o => new OptionView(o.OptionId, o.Text)).ToList(),
            session.QuestionStartedAt!.Value + session.QuizSet.TimeLimit, serverNow);
    }

    /// <summary>The open question, or null in the Lobby, a reveal or after the finish.</summary>
    public static QuestionView? OpenQuestion(Session session, DateTimeOffset serverNow) =>
        session.Phase == SessionPhase.Question ? Question(session, session.CurrentQuestion!.Id, serverNow) : null;

    public static IReadOnlyList<BoardEntry> Board(IReadOnlyList<RankedPlayer> ranked, int take = int.MaxValue) =>
        ranked.Take(take).Select(r => new BoardEntry(r.Rank, r.Player.DisplayName, r.TotalPoints, r.Player.Online)).ToList();

    /// <summary>Everyone who joined, in join order (the database returns them in key order).</summary>
    public static IReadOnlyList<PlayerView> Players(Session session) =>
        session.Players.OrderBy(p => p.JoinedAt).ThenBy(p => p.DisplayName, StringComparer.Ordinal)
            .Select(p => new PlayerView(p.DisplayName, p.Online)).ToList();

    public static YouView You(IReadOnlyList<RankedPlayer> ranked, Guid userId)
    {
        var mine = ranked.Single(r => r.Player.UserId == userId);
        return new YouView(mine.TotalPoints, mine.Rank);
    }
}
