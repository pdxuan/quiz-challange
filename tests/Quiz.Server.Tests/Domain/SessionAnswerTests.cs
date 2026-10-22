using Quiz.Server.Domain;
using static Quiz.Server.Tests.Domain.DomainBuilders;

namespace Quiz.Server.Tests.Domain;

public class SessionAnswerTests
{
    [Fact]
    public void SubmitAnswer_Correct_StoresAnswerWithPointsAndRaisesTotal()
    {
        var session = Running(2);

        var outcome = session.Answer(1, "a", TimeSpan.FromSeconds(3));

        var answer = Assert.Single(session.Answers);
        var player = session.Players.Single(p => p.UserId == User(1));
        Assert.Equal(new AnswerOutcome("q1", answer.SubmissionId), outcome);
        Assert.Equal(("q1", "a", 900, T0.AddSeconds(3)), (answer.QuestionId, answer.OptionId, answer.Points, answer.ReceivedAt));
        Assert.Equal(900, player.TotalPoints);
        Assert.Equal(T0.AddSeconds(3), player.ReachedAt);
    }

    [Fact]
    public void SubmitAnswer_Wrong_StoresZeroAndKeepsTotal()
    {
        var session = Running(2);

        session.Answer(1, "b", TimeSpan.FromSeconds(3));

        Assert.Equal(0, Assert.Single(session.Answers).Points);
        var player = session.Players.Single(p => p.UserId == User(1));
        Assert.Equal((0, T0), (player.TotalPoints, player.ReachedAt));
    }

    [Fact]
    public void SubmitAnswer_RaisesAnswersCountWithoutRevealingResults()
    {
        var session = Running(3);

        session.Answer(1, "a", TimeSpan.FromSeconds(1));

        Assert.Equal(new AnswersCountChanged("q1", 1, 3), Assert.Single(session.Events));
    }

    [Fact]
    public void SubmitAnswer_OneMsBeforeDeadline_IsAccepted()
    {
        var session = Running(2);

        session.Answer(1, "a", Limit - TimeSpan.FromMilliseconds(1));

        Assert.Equal(500, Assert.Single(session.Answers).Points);
    }

    [Fact]
    public void SubmitAnswer_AtDeadline_IsRejectedAsClosedAndStoresNothing()
    {
        var session = Running(2);

        Assert.Equal(SessionError.QuestionClosed, ErrorOf(() => session.Answer(1, "a", Limit)));
        Assert.Empty(session.Answers);
        Assert.Empty(session.Events);
    }

    [Fact]
    public void SubmitAnswer_SecondDifferentAnswer_IsRejectedAndFirstCounts()
    {
        var session = Running(2);
        session.Answer(1, "b", TimeSpan.FromSeconds(1));

        Assert.Equal(SessionError.AlreadyAnswered, ErrorOf(() => session.Answer(1, "a", TimeSpan.FromSeconds(2))));
        Assert.Equal("b", Assert.Single(session.Answers).OptionId);
        Assert.Equal(0, session.Players.Single(p => p.UserId == User(1)).TotalPoints);
    }

    [Fact]
    public void SubmitAnswer_RetryWithSameSubmissionId_ReturnsTheOriginalAndScoresOnce()
    {
        var session = Running(2);
        var submission = Guid.NewGuid();
        var first = session.Answer(1, "a", TimeSpan.FromSeconds(3), submission);
        session.ClearEvents();

        var retry = session.Answer(1, "a", TimeSpan.FromSeconds(4), submission);

        Assert.Equal(first, retry);
        Assert.Single(session.Answers);
        Assert.Equal(900, session.Players.Single(p => p.UserId == User(1)).TotalPoints);
        Assert.Empty(session.Events);
    }

    [Fact]
    public void SubmitAnswer_RetryWithSameSubmissionIdAfterTheDeadline_StillReturnsTheOriginal()
    {
        var session = Running(2);
        var submission = Guid.NewGuid();
        var first = session.Answer(1, "a", TimeSpan.FromSeconds(3), submission);

        Assert.Equal(first, session.Answer(1, "a", Limit + TimeSpan.FromSeconds(1), submission));
    }

    [Fact]
    public void SubmitAnswer_BeforeStart_IsNotStarted()
    {
        var session = Lobby(1);

        var error = ErrorOf(() => session.SubmitAnswer(User(1), "q1", "a", Guid.NewGuid(), T0, T0, Rules));

        Assert.Equal(SessionError.NotStarted, error);
        Assert.Empty(session.Answers);
    }

    [Fact]
    public void SubmitAnswer_ForAnotherQuestion_IsNotCurrentQuestion()
    {
        var session = Running(1);

        var error = ErrorOf(() => session.SubmitAnswer(User(1), "q2", "a", Guid.NewGuid(), T0, T0, Rules));

        Assert.Equal(SessionError.NotCurrentQuestion, error);
        Assert.Empty(session.Answers);
    }

    [Fact]
    public void SubmitAnswer_UnknownOption_IsInvalid()
    {
        var session = Running(1);

        Assert.Equal(SessionError.Invalid, ErrorOf(() => session.Answer(1, "z", TimeSpan.FromSeconds(1))));
        Assert.Empty(session.Answers);
    }

    [Fact]
    public void SubmitAnswer_EmptySubmissionId_IsInvalid()
    {
        var session = Running(1);

        Assert.Equal(SessionError.Invalid, ErrorOf(() => session.Answer(1, "a", TimeSpan.FromSeconds(1), Guid.Empty)));
    }

    [Fact]
    public void SubmitAnswer_UserNotJoined_IsNotJoined()
    {
        var session = Running(1);

        Assert.Equal(SessionError.NotJoined, ErrorOf(() => session.Answer(7, "a", TimeSpan.FromSeconds(1))));
        Assert.Empty(session.Answers);
    }

    [Fact]
    public void SubmitAnswer_LateJoinerOnTheCurrentQuestion_IsNotEligible()
    {
        var session = Running(1);
        session.Join(User(2), Name(2), Conn(2), T0.AddSeconds(2), Rules);

        Assert.Equal(SessionError.NotEligible, ErrorOf(() => session.Answer(2, "a", TimeSpan.FromSeconds(3))));
    }

    [Fact]
    public void SubmitAnswer_LateJoinerOnTheNextQuestion_IsAccepted()
    {
        var session = Running(1);
        session.Join(User(2), Name(2), Conn(2), T0.AddSeconds(2), Rules);
        session.Answer(1, "a", TimeSpan.FromSeconds(3)); // the only eligible player answered -> closes
        session.Tick(T0.AddSeconds(8), Rules);            // reveal over -> q2 starts at T0+8

        session.SubmitAnswer(User(2), "q2", "a", Guid.NewGuid(), T0.AddSeconds(9), T0.AddSeconds(9), Rules);

        Assert.Equal(967, session.Players.Single(p => p.UserId == User(2)).TotalPoints); // 14 s of 15 left
    }

    [Fact]
    public void SubmitAnswer_RejoinedPlayerOnTheOpenQuestion_IsAccepted()
    {
        var session = Running(2);
        session.Disconnect(Conn(1), T0.AddSeconds(1), Rules);
        session.Join(User(1), Name(1), "conn-1b", T0.AddSeconds(2), Rules);

        session.Answer(1, "a", TimeSpan.FromSeconds(3));

        Assert.Equal(900, session.Players.Single(p => p.UserId == User(1)).TotalPoints);
    }

    [Fact]
    public void SubmitAnswer_DuringReveal_IsQuestionClosed()
    {
        var session = Running(1);
        session.Answer(1, "a", TimeSpan.FromSeconds(1)); // closes q1, reveal starts

        Assert.Equal(SessionError.QuestionClosed, ErrorOf(() => session.SubmitAnswer(User(1), "q1", "b", Guid.NewGuid(), T0.AddSeconds(2), T0.AddSeconds(2), Rules)));
    }

    [Fact]
    public void Leaderboard_WhileTheQuestionIsOpen_LeavesOutItsPoints()
    {
        var session = Running(3, questions: 2);
        session.Answer(2, "a", TimeSpan.FromSeconds(1)); // q1: player02 first
        session.Answer(1, "a", TimeSpan.FromSeconds(2));
        session.Answer(3, "b", TimeSpan.FromSeconds(3)); // all answered -> closed
        session.Tick(T0.AddSeconds(8), Rules);           // q2 starts at T0+8
        session.SubmitAnswer(User(3), "q2", "a", Guid.NewGuid(), T0.AddSeconds(9), T0.AddSeconds(9), Rules);

        var board = session.Leaderboard();

        // player03's q2 points are hidden until q2 closes: still 0, ranked by join time like before.
        Assert.Equal([(Name(2), 967, 1), (Name(1), 933, 2), (Name(3), 0, 3)],
            board.Select(r => (r.Player.DisplayName, r.TotalPoints, r.Rank)));
    }

    [Fact]
    public void Leaderboard_AfterTheQuestionCloses_ShowsItsPoints()
    {
        var session = Running(2, questions: 2);
        session.Answer(1, "b", TimeSpan.FromSeconds(1));
        session.Answer(2, "a", TimeSpan.FromSeconds(2)); // closes q1

        var board = session.Leaderboard();

        Assert.Equal([(Name(2), 933), (Name(1), 0)], board.Select(r => (r.Player.DisplayName, r.TotalPoints)));
    }

    [Fact]
    public void SubmitAnswer_TwentyPlayers_TotalsAndRanksMatchTheExpectedValues()
    {
        var session = Running(20, questions: 1);

        // Player n answers at n * 500 ms; even players answer wrong.
        for (var n = 1; n <= 20; n++)
        {
            session.Answer(n, n % 2 == 0 ? "b" : "a", TimeSpan.FromMilliseconds(n * 500));
        }

        var expected = Enumerable.Range(1, 20)
            .Select(n => (Name: Name(n), Points: n % 2 == 0 ? 0 : Scoring.Points(true, Limit - TimeSpan.FromMilliseconds(n * 500), Limit)))
            .ToList();
        foreach (var (name, points) in expected)
        {
            Assert.Equal(points, session.Players.Single(p => p.DisplayName == name).TotalPoints);
        }
        var board = session.Leaderboard();
        Assert.Equal(
            expected.OrderByDescending(e => e.Points).ThenBy(e => e.Name).Select(e => e.Name),
            board.Select(r => r.Player.DisplayName));
        Assert.Equal(1, board[0].Rank);                                // player01: fastest correct
        Assert.All(board.Where(r => r.Player.TotalPoints == 0), r => Assert.Equal(11, r.Rank)); // 10 wrong share rank 11
    }
}
