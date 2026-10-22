using Quiz.Server.Domain;
using static Quiz.Server.Tests.Domain.DomainBuilders;

namespace Quiz.Server.Tests.Domain;

public class SessionLifecycleTests
{
    [Fact]
    public void Start_ByOrganizer_RunsTheFirstQuestionWithItsDeadline()
    {
        var session = Lobby(2);

        session.Start(Organizer, T0, Rules);

        Assert.Equal(SessionState.Running, session.State);
        Assert.Equal(SessionPhase.Question, session.Phase);
        Assert.Equal("q1", session.CurrentQuestion!.Id);
        Assert.Equal(T0, session.QuestionStartedAt);
        Assert.Equal(T0 + Limit, session.PhaseEndsAt);
        Assert.Equal(new QuestionStarted("q1", 0, 3, T0 + Limit), Assert.Single(session.Events));
    }

    [Fact]
    public void Create_WithAQuizSetWithoutQuestions_IsInvalid()
    {
        Assert.Equal(SessionError.Invalid, ErrorOf(() => Session.Create("ABC234", QuizSet(questions: 0), Organizer, T0)));
    }

    [Fact]
    public void Start_ByAnotherUser_IsNotOrganizer()
    {
        var session = Lobby(1);

        Assert.Equal(SessionError.NotOrganizer, ErrorOf(() => session.Start(User(1), T0, Rules)));
        Assert.Equal(SessionState.Lobby, session.State);
    }

    [Fact]
    public void Start_WithNoPlayers_IsNoPlayers()
    {
        var session = NewSession();

        Assert.Equal(SessionError.NoPlayers, ErrorOf(() => session.Start(Organizer, T0, Rules)));
    }

    [Fact]
    public void Start_WithOnlyOfflinePlayers_IsNoPlayers()
    {
        var session = Lobby(1);
        session.Disconnect(Conn(1), T0, Rules);

        Assert.Equal(SessionError.NoPlayers, ErrorOf(() => session.Start(Organizer, T0, Rules)));
        Assert.Equal(SessionState.Lobby, session.State);
    }

    [Fact]
    public void Start_WithOneOnlinePlayerWhenTwoAreNeeded_IsNoPlayers()
    {
        var session = Lobby(1);

        Assert.Equal(SessionError.NoPlayers, ErrorOf(() => session.Start(Organizer, T0, TwoToStart)));
        Assert.Equal(SessionState.Lobby, session.State);
    }

    [Fact]
    public void Start_WithTwoJoinedButOneOffline_IsNoPlayers()
    {
        var session = Lobby(2);
        session.Disconnect(Conn(2), T0, TwoToStart);

        Assert.Equal(SessionError.NoPlayers, ErrorOf(() => session.Start(Organizer, T0, TwoToStart)));
    }

    [Fact]
    public void Start_WithTwoOnlinePlayersWhenTwoAreNeeded_Runs()
    {
        var session = Lobby(2);

        session.Start(Organizer, T0, TwoToStart);

        Assert.Equal(SessionState.Running, session.State);
    }

    [Fact]
    public void Start_Twice_IsNotInLobby()
    {
        var session = Running(1);

        Assert.Equal(SessionError.NotInLobby, ErrorOf(() => session.Start(Organizer, T0.AddSeconds(1), Rules)));
    }

    [Fact]
    public void EnsureOrganizer_RejectsOtherUsers()
    {
        var session = Lobby(1);

        session.EnsureOrganizer(Organizer);
        Assert.Equal(SessionError.NotOrganizer, ErrorOf(() => session.EnsureOrganizer(User(1))));
    }

    [Fact]
    public void Tick_BeforeTheDeadline_ChangesNothing()
    {
        var session = Running(2);

        session.Tick(T0 + Limit - TimeSpan.FromMilliseconds(1), Rules);

        Assert.Equal(SessionPhase.Question, session.Phase);
        Assert.Empty(session.Events);
    }

    [Fact]
    public void Tick_AtTheDeadline_ClosesTheQuestionAndStartsTheReveal()
    {
        var session = Running(2);

        session.Tick(T0 + Limit, Rules);

        Assert.Equal(SessionPhase.Reveal, session.Phase);
        Assert.Equal(T0 + Limit + Rules.RevealDuration, session.PhaseEndsAt);
        Assert.Equal(new QuestionClosed("q1", "a"), Assert.Single(session.Events));
    }

    [Fact]
    public void Tick_AfterTheReveal_StartsTheNextQuestion()
    {
        var session = Running(2);
        session.Tick(T0 + Limit, Rules);
        session.ClearEvents();
        var revealEnd = T0 + Limit + Rules.RevealDuration;

        session.Tick(revealEnd, Rules);

        Assert.Equal(("q2", SessionPhase.Question, revealEnd, revealEnd + Limit),
            (session.CurrentQuestion!.Id, session.Phase, session.QuestionStartedAt, session.PhaseEndsAt));
        Assert.Equal(new QuestionStarted("q2", 1, 3, revealEnd + Limit), Assert.Single(session.Events));
    }

    [Fact]
    public void Tick_AfterTheLastReveal_FinishesTheQuiz()
    {
        var session = Running(1, questions: 1);
        session.Tick(T0 + Limit, Rules);
        session.ClearEvents();
        var revealEnd = T0 + Limit + Rules.RevealDuration;

        session.Tick(revealEnd, Rules);

        Assert.Equal((SessionState.Finished, SessionPhase.None, revealEnd), (session.State, session.Phase, session.FinishedAt));
        Assert.Equal(new QuizFinished(), Assert.Single(session.Events));
    }

    [Fact]
    public void Tick_LongAfterTheDeadline_MakesOneTransitionAtATime()
    {
        var session = Running(2);
        var late = T0 + TimeSpan.FromMinutes(10);

        session.Tick(late, Rules);

        Assert.Equal(SessionPhase.Reveal, session.Phase);
        Assert.Equal(late + Rules.RevealDuration, session.PhaseEndsAt);
    }

    [Fact]
    public void SubmitAnswer_LastEligibleOnlinePlayer_ClosesTheQuestionEarly()
    {
        var session = Running(2);
        session.Answer(1, "a", TimeSpan.FromSeconds(1));
        session.ClearEvents();

        session.Answer(2, "b", TimeSpan.FromSeconds(2));

        Assert.Equal(SessionPhase.Reveal, session.Phase);
        Assert.Equal(T0.AddSeconds(2) + Rules.RevealDuration, session.PhaseEndsAt);
        Assert.Equal([new AnswersCountChanged("q1", 2, 2), new QuestionClosed("q1", "a")], session.Events);
    }

    [Fact]
    public void SubmitAnswer_ALateJoinerOnline_DoesNotKeepTheQuestionOpen()
    {
        var session = Running(1);
        session.Join(User(2), Name(2), Conn(2), T0.AddSeconds(1), Rules);

        session.Answer(1, "a", TimeSpan.FromSeconds(2));

        Assert.Equal(SessionPhase.Reveal, session.Phase);
    }

    [Fact]
    public void Disconnect_MidQuestion_MarksOfflineKeepsOnBoardAndClosesWhenTheRestAnswered()
    {
        var session = Running(3);
        session.Answer(1, "a", TimeSpan.FromSeconds(1));
        session.Answer(2, "a", TimeSpan.FromSeconds(2));
        session.ClearEvents();

        session.Disconnect(Conn(3), T0.AddSeconds(3), Rules);

        var player3 = session.Players.Single(p => p.UserId == User(3));
        Assert.False(player3.Online);
        Assert.Null(player3.ConnectionId);
        Assert.Contains(session.Leaderboard(), r => r.Player.UserId == User(3));
        Assert.Equal(SessionPhase.Reveal, session.Phase);
        Assert.Equal([new PlayerOnlineChanged(User(3), Name(3), false), new QuestionClosed("q1", "a")], session.Events);
    }

    [Fact]
    public void Disconnect_EligiblePlayerWhileTheQuestionStaysOpen_RaisesTheNewAnswersCount()
    {
        var session = Running(3);
        session.Answer(1, "a", TimeSpan.FromSeconds(1));
        session.ClearEvents();

        session.Disconnect(Conn(3), T0.AddSeconds(2), Rules);

        Assert.Equal(SessionPhase.Question, session.Phase);
        Assert.Equal([new PlayerOnlineChanged(User(3), Name(3), false), new AnswersCountChanged("q1", 1, 2)], session.Events);
    }

    [Fact]
    public void Disconnect_LastOnlinePlayerWhileRunning_FinishesAtOnce()
    {
        var session = Running(2);
        session.Disconnect(Conn(1), T0.AddSeconds(1), Rules);
        session.ClearEvents();

        session.Disconnect(Conn(2), T0.AddSeconds(2), Rules);

        Assert.Equal((SessionState.Finished, T0.AddSeconds(2)), (session.State, session.FinishedAt));
        Assert.Equal(2, session.Players.Count);
        Assert.Equal([new PlayerOnlineChanged(User(2), Name(2), false), new QuestionClosed("q1", "a"), new QuizFinished()], session.Events);
    }

    [Fact]
    public void Disconnect_LastOnlinePlayerDuringReveal_FinishesWithoutClosingAgain()
    {
        var session = Running(1);
        session.Answer(1, "a", TimeSpan.FromSeconds(1));
        session.ClearEvents();

        session.Disconnect(Conn(1), T0.AddSeconds(2), Rules);

        Assert.Equal(SessionState.Finished, session.State);
        Assert.Equal([new PlayerOnlineChanged(User(1), Name(1), false), new QuizFinished()], session.Events);
    }

    [Fact]
    public void Disconnect_InLobby_KeepsThePlayerOffline()
    {
        var session = Lobby(1);

        session.Disconnect(Conn(1), T0, Rules);

        Assert.Equal(SessionState.Lobby, session.State);
        Assert.False(Assert.Single(session.Players).Online);
    }

    [Fact]
    public void Disconnect_UnknownOrReplacedConnection_IsIgnored()
    {
        var session = Lobby(1);
        session.Join(User(1), Name(1), "conn-1b", T0, Rules); // second tab replaced conn-1
        session.ClearEvents();

        session.Disconnect(Conn(1), T0, Rules);

        Assert.True(Assert.Single(session.Players).Online);
        Assert.Empty(session.Events);
    }

    [Fact]
    public void MarkAllOffline_AfterRestart_ClosesAnOverdueQuestionAndKeepsTheRevealOpenForRejoins()
    {
        var session = Running(2);
        var restartedAt = T0 + TimeSpan.FromMinutes(2); // the deadline passed while the server was down

        session.MarkAllOffline();
        Assert.All(session.Players, p => Assert.False(p.Online));
        session.Tick(restartedAt, Rules);

        Assert.Equal((SessionState.Running, SessionPhase.Reveal), (session.State, session.Phase));
        Assert.Equal(restartedAt + Rules.RevealDuration, session.PhaseEndsAt);
        Assert.Equal(new QuestionClosed("q1", "a"), Assert.Single(session.Events));
    }

    [Fact]
    public void MarkAllOffline_NobodyRejoinsDuringTheReveal_FinishesAtTheRevealEnd()
    {
        var session = Running(2);
        session.MarkAllOffline();
        session.Tick(T0 + Limit, Rules);
        session.ClearEvents();

        session.Tick(T0 + Limit + Rules.RevealDuration, Rules);

        Assert.Equal(SessionState.Finished, session.State);
        Assert.Equal(new QuizFinished(), Assert.Single(session.Events));
    }

    [Fact]
    public void MarkAllOffline_ARejoinDuringTheReveal_StartsTheNextQuestion()
    {
        var session = Running(2);
        session.MarkAllOffline();
        session.Tick(T0 + Limit, Rules);
        session.Join(User(1), Name(1), "conn-1b", T0 + Limit + TimeSpan.FromSeconds(1), Rules);

        session.Tick(T0 + Limit + Rules.RevealDuration, Rules);

        Assert.Equal((SessionState.Running, SessionPhase.Question, "q2"), (session.State, session.Phase, session.CurrentQuestion!.Id));
        Assert.True(session.Players.Single(p => p.UserId == User(1)).Online);
    }

    [Fact]
    public void MarkAllOffline_ThenARejoinDuringTheQuestion_KeepsTheQuizGoing()
    {
        var session = Running(2);
        session.MarkAllOffline();
        session.Join(User(1), Name(1), "conn-1b", T0.AddSeconds(1), Rules);

        session.Tick(T0 + Limit, Rules);

        Assert.Equal((SessionState.Running, SessionPhase.Reveal), (session.State, session.Phase));
    }

    [Fact]
    public void Resume_DuringAnOverdueReveal_GivesPlayersAFullRevealToRejoin()
    {
        var session = Running(1);
        session.Answer(1, "a", TimeSpan.FromSeconds(1)); // closed; the reveal ended at T0+6 while the server was down
        var restartedAt = T0 + TimeSpan.FromMinutes(2);

        session.Resume(restartedAt, Rules);

        Assert.Equal((SessionPhase.Reveal, restartedAt + Rules.RevealDuration), (session.Phase, session.PhaseEndsAt));
        Assert.False(Assert.Single(session.Players).Online);
    }

    [Fact]
    public void Resume_DuringAQuestion_KeepsItsDeadline()
    {
        var session = Running(2);

        session.Resume(T0.AddSeconds(3), Rules);

        Assert.Equal((SessionPhase.Question, T0 + Limit), (session.Phase, session.PhaseEndsAt));
        Assert.All(session.Players, p => Assert.False(p.Online));
    }

    [Fact]
    public void FullQuiz_EndsFinishedWithEveryQuestionPlayed()
    {
        var session = Running(1, questions: 2);
        var now = T0;
        var started = new List<string>();

        while (session.State == SessionState.Running)
        {
            if (session.Phase == SessionPhase.Question)
            {
                started.Add(session.CurrentQuestion!.Id);
            }
            now = session.PhaseEndsAt!.Value;
            session.Tick(now, Rules);
        }

        Assert.Equal(["q1", "q2"], started);
        Assert.Equal(SessionState.Finished, session.State);
    }

    [Fact]
    public void Finished_RejectsAnswers()
    {
        var session = Running(1, questions: 1);
        session.Disconnect(Conn(1), T0.AddSeconds(1), Rules);

        Assert.Equal(SessionError.QuestionClosed, ErrorOf(() => session.SubmitAnswer(User(1), "q1", "a", Guid.NewGuid(), T0.AddSeconds(2), T0.AddSeconds(2), Rules)));
    }
}
