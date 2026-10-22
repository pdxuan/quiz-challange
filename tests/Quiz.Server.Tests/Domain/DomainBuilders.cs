using Quiz.Server.Domain;

namespace Quiz.Server.Tests.Domain;

/// <summary>Builds quiz sets and sessions for domain tests. Every question's correct option is "a".</summary>
internal static class DomainBuilders
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    public static readonly Guid Organizer = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly SessionRules Rules = new(Capacity: 20, RevealDuration: TimeSpan.FromSeconds(5));
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    public static QuizSet QuizSet(int questions = 3, int timeLimitSec = 15)
    {
        var set = new QuizSet("set-1", "Animals", timeLimitSec);
        for (var i = 0; i < questions; i++)
        {
            var id = $"q{i + 1}";
            set.Questions.Add(new Question(id, set.Id, i, $"Word {i + 1}", QuestionDirection.WordToMeaning, "a",
            [
                new Option("a", id, 0, "right"),
                new Option("b", id, 1, "wrong 1"),
                new Option("c", id, 2, "wrong 2"),
                new Option("d", id, 3, "wrong 3"),
            ]));
        }
        return set;
    }

    public static Guid User(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    public static string Name(int n) => $"player{n:D2}";

    public static string Conn(int n) => $"conn-{n}";

    public static Session NewSession(int questions = 3) =>
        Session.Create("ABC234", QuizSet(questions), Organizer, T0);

    /// <summary>A session in Lobby with players 1..count joined (all online) at T0.</summary>
    public static Session Lobby(int count, int questions = 3)
    {
        var session = NewSession(questions);
        for (var n = 1; n <= count; n++)
        {
            session.Join(User(n), Name(n), Conn(n), T0, Rules);
        }
        session.ClearEvents();
        return session;
    }

    /// <summary>A session whose first question started at T0 with players 1..count.</summary>
    public static Session Running(int count, int questions = 3)
    {
        var session = Lobby(count, questions);
        session.Start(Organizer, T0);
        session.ClearEvents();
        return session;
    }

    public static AnswerOutcome Answer(this Session session, int n, string optionId, TimeSpan after, Guid? submissionId = null) =>
        session.SubmitAnswer(User(n), session.CurrentQuestion!.Id, optionId, submissionId ?? Guid.NewGuid(), T0 + after, T0 + after, Rules);

    public static SessionError ErrorOf(Action act) => Assert.Throws<SessionException>(act).Error;
}
