using Quiz.Server.Domain;
using static Quiz.Server.Tests.Domain.DomainBuilders;

namespace Quiz.Server.Tests.Domain;

public class SessionJoinTests
{
    [Fact]
    public void Create_StartsInLobbyWithTheCallerAsOrganizer()
    {
        var session = NewSession();

        Assert.Equal("ABC234", session.QuizId);
        Assert.Equal(SessionState.Lobby, session.State);
        Assert.Equal(Organizer, session.OrganizerUserId);
        Assert.Equal(-1, session.CurrentIndex);
        Assert.Null(session.CurrentQuestion);
    }

    [Fact]
    public void Join_InLobby_AddsOnlinePlayerAndRaisesPlayerJoined()
    {
        var session = NewSession();

        var outcome = session.Join(User(1), "alice", Conn(1), T0, Rules);

        var player = Assert.Single(session.Players);
        Assert.Equal(("alice", Conn(1), true, 0, T0), (player.DisplayName, player.ConnectionId, player.Online, player.TotalPoints, player.JoinedAt));
        Assert.False(outcome.Rejoined);
        Assert.False(outcome.CanAnswerCurrent);
        Assert.Equal(new PlayerJoined(User(1), "alice", 1), Assert.Single(session.Events));
    }

    [Fact]
    public void Join_Organizer_PlaysLikeAnyoneElse()
    {
        var session = NewSession();

        var outcome = session.Join(Organizer, "org", Conn(9), T0, Rules);

        Assert.Equal((Organizer, false), (Assert.Single(session.Players).UserId, outcome.Rejoined));
        Assert.Equal(new PlayerJoined(Organizer, "org", 1), Assert.Single(session.Events));
    }

    [Fact]
    public void Join_WhenFull_RejectsANewUser()
    {
        var session = Lobby(20);

        Assert.Equal(SessionError.SessionFull, ErrorOf(() => session.Join(User(21), Name(21), Conn(21), T0, Rules)));
        Assert.Equal(20, session.Players.Count);
    }

    [Fact]
    public void Join_WhenFull_OfflinePlayersStillCount()
    {
        var session = Lobby(20);
        session.Disconnect(Conn(1), T0, Rules);

        Assert.Equal(SessionError.SessionFull, ErrorOf(() => session.Join(User(21), Name(21), Conn(21), T0, Rules)));
    }

    [Fact]
    public void Join_WhenFull_AnExistingUserCanStillRejoin()
    {
        var session = Lobby(20);
        session.Disconnect(Conn(1), T0, Rules);

        var outcome = session.Join(User(1), Name(1), "conn-1b", T0.AddSeconds(1), Rules);

        Assert.True(outcome.Rejoined);
        Assert.Equal(20, session.Players.Count);
    }

    [Fact]
    public void Join_FinishedSession_IsRejected()
    {
        var session = Running(1, questions: 1);
        session.Disconnect(Conn(1), T0.AddSeconds(1), Rules); // last player leaves -> finished

        Assert.Equal(SessionError.QuizFinished, ErrorOf(() => session.Join(User(2), Name(2), Conn(2), T0.AddSeconds(2), Rules)));
    }

    [Theory]
    [InlineData("", "conn")]
    [InlineData("  ", "conn")]
    [InlineData("alice", "")]
    public void Join_MissingNameOrConnection_IsInvalid(string name, string connectionId)
    {
        var session = NewSession();

        Assert.Equal(SessionError.Invalid, ErrorOf(() => session.Join(User(1), name, connectionId, T0, Rules)));
    }

    [Fact]
    public void Join_SameUserAgain_IsTheSamePlayerBackOnlineWithPointsKept()
    {
        var session = Running(2);
        session.Answer(1, "a", TimeSpan.FromSeconds(3));
        session.Disconnect(Conn(1), T0.AddSeconds(4), Rules);
        session.ClearEvents();

        var outcome = session.Join(User(1), Name(1), "conn-1b", T0.AddSeconds(5), Rules);

        var player = session.Players.Single(p => p.UserId == User(1));
        Assert.True(outcome.Rejoined);
        Assert.Null(outcome.ReplacedConnectionId);
        Assert.Equal(2, session.Players.Count);
        Assert.True(player.Online);
        Assert.Equal("conn-1b", player.ConnectionId);
        Assert.Equal(900, player.TotalPoints);
        Assert.Equal([new PlayerOnlineChanged(User(1), Name(1), true), new AnswersCountChanged("q1", 1, 2)], session.Events);
    }

    [Fact]
    public void Join_SameConnectionAgain_ReplacesNothing()
    {
        var session = Lobby(1);

        var outcome = session.Join(User(1), Name(1), Conn(1), T0.AddSeconds(1), Rules);

        Assert.True(outcome.Rejoined);
        Assert.Null(outcome.ReplacedConnectionId);
        Assert.Equal(Conn(1), Assert.Single(session.Players).ConnectionId);
    }

    [Fact]
    public void Join_RejoinInLobby_RaisesNoAnswersCount()
    {
        var session = Lobby(1);
        session.Disconnect(Conn(1), T0, Rules);
        session.ClearEvents();

        session.Join(User(1), Name(1), "conn-1b", T0.AddSeconds(1), Rules);

        Assert.Equal(new PlayerOnlineChanged(User(1), Name(1), true), Assert.Single(session.Events));
    }

    [Fact]
    public void Join_SameUserFromASecondTab_ReplacesTheOldConnection()
    {
        var session = Lobby(1);

        var outcome = session.Join(User(1), Name(1), "conn-1b", T0.AddSeconds(1), Rules);

        Assert.True(outcome.Rejoined);
        Assert.Equal(Conn(1), outcome.ReplacedConnectionId);
        Assert.Equal("conn-1b", Assert.Single(session.Players).ConnectionId);
        Assert.Empty(session.Events);
    }

    [Fact]
    public void Join_LateWhileRunning_StartsAtZeroAndCannotAnswerTheCurrentQuestion()
    {
        var session = Running(1);

        var outcome = session.Join(User(2), Name(2), Conn(2), T0.AddSeconds(2), Rules);

        Assert.False(outcome.Rejoined);
        Assert.False(outcome.CanAnswerCurrent);
        Assert.Equal(0, session.Players.Single(p => p.UserId == User(2)).TotalPoints);
    }

    [Fact]
    public void Join_RejoinDuringAnOpenQuestionNotYetAnswered_CanAnswerCurrent()
    {
        var session = Running(2);
        session.Disconnect(Conn(1), T0.AddSeconds(1), Rules);

        var outcome = session.Join(User(1), Name(1), "conn-1b", T0.AddSeconds(2), Rules);

        Assert.True(outcome.CanAnswerCurrent);
    }
}
