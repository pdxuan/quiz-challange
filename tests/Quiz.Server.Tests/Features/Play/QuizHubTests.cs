using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quiz.Server.Domain;
using Quiz.Server.Observability;
using Quiz.Server.Tests.TestHost;

namespace Quiz.Server.Tests.Features.Play;

/// <summary>Real SignalR clients against the test server; the clock is moved by the test.</summary>
public class QuizHubTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Reveal = TimeSpan.FromSeconds(5);

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;
    private static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();
    private static List<string> Names(JsonElement joinResult) => joinResult.GetProperty("players").EnumerateArray().Select(p => Str(p, "username")).ToList();

    /// <summary>A Lobby organized by "org" with the named players created (not joined).</summary>
    private static async Task<string> LobbyAsync(QuizApiFactory factory, int questions, params string[] players)
    {
        var (quizId, _) = await factory.AddLobbyAsync(questions);
        foreach (var name in players)
        {
            await factory.AddUserAsync(name);
        }
        return quizId;
    }

    private static async Task<HubProbe> JoinedAsync(QuizApiFactory factory, string quizId, string name)
    {
        var probe = await HubProbe.ConnectAsync(factory, name);
        await probe.InvokeAsync("JoinQuiz", quizId);
        return probe;
    }

    private static async Task StartAsync(QuizApiFactory factory, string quizId)
    {
        var response = await (await factory.LoginAsync("org")).PostAsync($"/api/sessions/{quizId}/start", null);
        response.EnsureSuccessStatusCode();
    }

    private static Task<JsonElement> AnswerAsync(HubProbe probe, string quizId, string questionId, string optionId, Guid? submissionId = null) =>
        probe.InvokeAsync("SubmitAnswer", quizId, questionId, optionId, submissionId ?? Guid.NewGuid());

    [Fact]
    public async Task Connect_Anonymous_IsRefused()
    {
        using var factory = new QuizApiFactory();

        var error = await Assert.ThrowsAnyAsync<HttpRequestException>(() => HubProbe.ConnectAsync(factory, null));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task JoinQuiz_InLobby_ReturnsTheJoinResultAndPushesPlayerJoined()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 3, "alice", "bob");
        await using var alice = await JoinedAsync(factory, quizId, "alice");
        await using var bob = await HubProbe.ConnectAsync(factory, "bob");

        var result = await bob.InvokeAsync("JoinQuiz", quizId.ToLowerInvariant()); // IDs are case-insensitive

        Assert.Equal(("bob", "Lobby", 2, false), (Str(result, "username"), Str(result, "state"), Int(result, "playerCount"), result.GetProperty("canAnswerCurrent").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("currentQuestion").ValueKind);
        var joined = await alice.NextAsync("PlayerJoined", e => Str(e, "username") == "bob");
        Assert.Equal(2, Int(joined, "playerCount"));
        await using var db = factory.CreateDbContext();
        Assert.Equal(2, await db.Players.CountAsync(p => p.QuizId == quizId));
    }

    [Fact]
    public async Task JoinQuiz_UnknownQuiz_FailsWithQuizNotFound()
    {
        using var factory = new QuizApiFactory();
        await factory.AddUserAsync("alice");
        await using var alice = await HubProbe.ConnectAsync(factory, "alice");

        Assert.Equal("QuizNotFound", await alice.ErrorOfAsync("JoinQuiz", "ZZZ999"));
        Assert.Equal("QuizNotFound", await alice.ErrorOfAsync("JoinQuiz", "not an id"));
    }

    [Fact]
    public async Task JoinQuiz_ByTheOrganizer_IsAPlayerAndOnlyTheOrganizerIsToldSo()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 3, "bob");
        await using var org = await HubProbe.ConnectAsync(factory, "org");
        await using var bob = await HubProbe.ConnectAsync(factory, "bob");

        var asOrganizer = await org.InvokeAsync("JoinQuiz", quizId);
        await factory.AdvanceAsync(TimeSpan.FromSeconds(1)); // bob joins later, so he is listed second
        var asPlayer = await bob.InvokeAsync("JoinQuiz", quizId);

        Assert.True(asOrganizer.GetProperty("isOrganizer").GetBoolean());
        Assert.False(asPlayer.GetProperty("isOrganizer").GetBoolean());
        Assert.Equal(["org"], Names(asOrganizer));
        Assert.Equal(["org", "bob"], Names(asPlayer)); // join order
        await using var db = factory.CreateDbContext();
        Assert.Equal(2, await db.Players.CountAsync(p => p.QuizId == quizId));
    }

    [Fact]
    public async Task Organizer_PlaysWithAnotherPlayerAndScoresLikeThem()
    {
        using var factory = new QuizApiFactory { MinPlayersToStart = null }; // the shipped minimum: two online players
        var quizId = await LobbyAsync(factory, 1, "bob");
        await using var org = await JoinedAsync(factory, quizId, "org");
        await using var bob = await JoinedAsync(factory, quizId, "bob");

        await StartAsync(factory, quizId);
        var questionId = Str(await org.NextAsync("QuestionStarted"), "questionId");
        await AnswerAsync(bob, quizId, questionId, "b");
        await AnswerAsync(org, quizId, questionId, "a"); // both eligible players answered: closes at once

        var closed = await org.NextAsync("QuestionClosed");
        Assert.Equal((true, 1000, 1), (closed.GetProperty("correct").GetBoolean(), Int(closed, "points"), Int(closed, "rank")));
    }

    [Fact]
    public async Task JoinQuiz_The21stUser_FailsWithSessionFull()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 3, "late");
        for (var i = 0; i < 20; i++)
        {
            await factory.JoinAsync(quizId, await factory.AddUserAsync($"p{i:D2}"), $"p{i:D2}");
        }
        await using var late = await HubProbe.ConnectAsync(factory, "late");

        Assert.Equal("SessionFull", await late.ErrorOfAsync("JoinQuiz", quizId));
    }

    [Fact]
    public async Task FinishedQuiz_RejectsJoinsAndAnswers()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 1, "alice", "bob");
        await using var alice = await JoinedAsync(factory, quizId, "alice");
        await StartAsync(factory, quizId);
        var questionId = Str(await alice.NextAsync("QuestionStarted"), "questionId");
        await factory.AdvanceAsync(Limit);
        await factory.AdvanceAsync(Reveal);
        await alice.NextAsync("QuizFinished");
        await using var bob = await HubProbe.ConnectAsync(factory, "bob");

        Assert.Equal("QuizFinished", await bob.ErrorOfAsync("JoinQuiz", quizId));
        Assert.Equal("QuestionClosed", await alice.ErrorOfAsync("SubmitAnswer", quizId, questionId, "a", Guid.NewGuid()));
    }

    [Fact]
    public async Task FinishedQuiz_LeaderboardStaysReadableAfterARestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiz-tests", Guid.NewGuid().ToString("N"));
        string quizId;
        using (var before = new QuizApiFactory { Root = root, DeleteFilesOnDispose = false })
        {
            quizId = await LobbyAsync(before, 1, "alice", "bob");
            await using var alice = await JoinedAsync(before, quizId, "alice");
            await using var bob = await JoinedAsync(before, quizId, "bob");
            await StartAsync(before, quizId);
            var questionId = Str(await alice.NextAsync("QuestionStarted"), "questionId");
            await AnswerAsync(alice, quizId, questionId, "a");
            await AnswerAsync(bob, quizId, questionId, "b"); // closes
            await before.AdvanceAsync(Reveal);
            await alice.NextAsync("QuizFinished");
        }

        using var after = new QuizApiFactory { Root = root };
        var board = await (await after.LoginAsync("bob")).GetFromJsonAsync<JsonElement>($"/api/sessions/{quizId}/leaderboard");

        Assert.Equal("Finished", Str(board, "state"));
        var entries = board.GetProperty("entries").EnumerateArray().Select(e => (Int(e, "rank"), Str(e, "username"), Int(e, "totalPoints"))).ToList();
        Assert.Equal([(1, "alice", 1000), (2, "bob", 0)], entries);
    }

    [Fact]
    public async Task LateJoin_GetsTheCurrentQuestionButCannotAnswerItThenAnswersTheNext()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 2, "alice", "late");
        await using var alice = await JoinedAsync(factory, quizId, "alice");
        await StartAsync(factory, quizId);
        await factory.AdvanceAsync(TimeSpan.FromSeconds(1)); // joins after the question started
        await using var late = await HubProbe.ConnectAsync(factory, "late");

        var result = await late.InvokeAsync("JoinQuiz", quizId);

        Assert.False(result.GetProperty("canAnswerCurrent").GetBoolean());
        var current = result.GetProperty("currentQuestion");
        Assert.False(current.TryGetProperty("correctOptionId", out _));
        Assert.Equal("NotEligible", await late.ErrorOfAsync("SubmitAnswer", quizId, Str(current, "questionId"), "a", Guid.NewGuid()));
        await AnswerAsync(alice, quizId, Str(current, "questionId"), "a"); // closes q1
        await factory.AdvanceAsync(Reveal);
        var next = await late.NextAsync("QuestionStarted", e => Int(e, "index") == 1);
        await AnswerAsync(late, quizId, Str(next, "questionId"), "a");
    }

    [Fact]
    public async Task Rejoin_FromASecondConnection_IsTheSamePlayerAndTheOldConnectionStopsReceiving()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 3, "alice", "bob");
        await using var first = await JoinedAsync(factory, quizId, "alice");
        await first.NextAsync("PlayerJoined"); // its own join
        await using var second = await HubProbe.ConnectAsync(factory, "alice");

        var result = await second.InvokeAsync("JoinQuiz", quizId);
        await using var bob = await JoinedAsync(factory, quizId, "bob");

        Assert.Equal(1, Int(result, "playerCount"));
        await second.NextAsync("PlayerJoined", e => Str(e, "username") == "bob");
        Assert.False(await first.ReceivesAsync("PlayerJoined", TimeSpan.FromMilliseconds(500)), "the replaced connection still receives session events");
        await using var db = factory.CreateDbContext();
        Assert.Equal(2, await db.Players.CountAsync(p => p.QuizId == quizId));
    }

    [Fact]
    public async Task SubmitAnswer_ReturnsOnlyAcceptedCommitsTheRowAndMergesAnswersCount()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 3, "alice", "bob", "carol");
        await using var alice = await JoinedAsync(factory, quizId, "alice");
        await using var bob = await JoinedAsync(factory, quizId, "bob");
        await using var carol = await JoinedAsync(factory, quizId, "carol");
        await StartAsync(factory, quizId);
        var questionId = Str(await carol.NextAsync("QuestionStarted"), "questionId");
        var submission = Guid.NewGuid();

        var accepted = await AnswerAsync(alice, quizId, questionId, "a", submission);
        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(1000, (await db.Answers.SingleAsync()).Points); // committed before the reply
        }
        await AnswerAsync(bob, quizId, questionId, "b");

        Assert.Equal([questionId, submission.ToString()], [Str(accepted, "questionId"), Str(accepted, "submissionId")]);
        Assert.Equal(["questionId", "submissionId"], accepted.EnumerateObject().Select(p => p.Name)); // no correctness, no points
        var first = await carol.NextAsync("AnswersCount");
        Assert.Equal((1, 3), (Int(first, "answered"), Int(first, "eligible")));
        Assert.False(await carol.ReceivesAsync("AnswersCount", TimeSpan.FromMilliseconds(300)), "a second count inside 200 ms was not merged");
        await factory.AdvanceAsync(TimeSpan.FromMilliseconds(200));
        Assert.Equal(2, Int(await carol.NextAsync("AnswersCount"), "answered"));
    }

    [Fact]
    public async Task SubmitAnswer_OneMillisecondBeforeTheDeadlineIsAcceptedAndAtTheDeadlineIsClosed()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 3, "alice", "bob", "carol");
        await using var alice = await JoinedAsync(factory, quizId, "alice");
        await using var bob = await JoinedAsync(factory, quizId, "bob");
        await using var carol = await JoinedAsync(factory, quizId, "carol");
        await StartAsync(factory, quizId);
        var questionId = Str(await alice.NextAsync("QuestionStarted"), "questionId");

        await factory.AdvanceAsync(Limit - TimeSpan.FromMilliseconds(1));
        await AnswerAsync(alice, quizId, questionId, "a");
        await factory.AdvanceAsync(TimeSpan.FromMilliseconds(1));

        Assert.Equal("QuestionClosed", await bob.ErrorOfAsync("SubmitAnswer", quizId, questionId, "a", Guid.NewGuid()));
        await using var db = factory.CreateDbContext();
        Assert.Equal(500, (await db.Answers.SingleAsync()).Points);
    }

    [Fact]
    public async Task SubmitAnswer_TwoAnswersFromOneUserOnTwoConnectionsAtOnce_StoreOneAndARetryKeepsTheScore()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 3, "alice", "bob");
        await using var tab1 = await JoinedAsync(factory, quizId, "alice");
        await using var tab2 = await JoinedAsync(factory, quizId, "alice");
        await using var bob = await JoinedAsync(factory, quizId, "bob");
        await StartAsync(factory, quizId);
        var questionId = Str(await bob.NextAsync("QuestionStarted"), "questionId");

        var results = await Task.WhenAll(
            AnswerAsync(tab1, quizId, questionId, "a").ContinueWith(t => t),
            AnswerAsync(tab2, quizId, questionId, "b").ContinueWith(t => t));

        Assert.Equal(1, results.Count(t => t.IsCompletedSuccessfully));
        Assert.Contains("HubException: AlreadyAnswered", results.Single(t => t.IsFaulted).Exception!.InnerException!.Message);
        await using var db = factory.CreateDbContext();
        var stored = await db.Answers.SingleAsync();
        var retry = await AnswerAsync(tab1, quizId, questionId, stored.OptionId, stored.SubmissionId); // a client retry of the stored answer
        Assert.Equal(stored.SubmissionId.ToString(), Str(retry, "submissionId"));
        await using var after = factory.CreateDbContext();
        Assert.Single(await after.Answers.ToListAsync());
        Assert.Equal(stored.Points, (await after.Players.SingleAsync(p => p.DisplayName == "alice")).TotalPoints);
    }

    [Fact]
    public async Task SubmitAnswer_RejectionCodes()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 3, "alice", "stranger");
        await using var alice = await JoinedAsync(factory, quizId, "alice");
        await using var stranger = await HubProbe.ConnectAsync(factory, "stranger");

        Assert.Equal("NotStarted", await alice.ErrorOfAsync("SubmitAnswer", quizId, "x", "a", Guid.NewGuid()));
        await StartAsync(factory, quizId);
        var questionId = Str(await alice.NextAsync("QuestionStarted"), "questionId");
        Assert.Equal("NotCurrentQuestion", await alice.ErrorOfAsync("SubmitAnswer", quizId, "another-question", "a", Guid.NewGuid()));
        Assert.Equal("Invalid", await alice.ErrorOfAsync("SubmitAnswer", quizId, questionId, "z", Guid.NewGuid()));
        Assert.Equal("Invalid", await alice.ErrorOfAsync("SubmitAnswer", quizId, questionId, "a", Guid.Empty));
        Assert.Equal("NotJoined", await stranger.ErrorOfAsync("SubmitAnswer", quizId, questionId, "a", Guid.NewGuid()));
        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.Answers.ToListAsync());
    }

    [Fact]
    public async Task TwentyPlayers_AnswerAtOnce_EachGetsTheirExactResultAndFinalRank()
    {
        using var factory = new QuizApiFactory();
        var names = Enumerable.Range(1, 20).Select(i => $"player{i:D2}").ToArray();
        var quizId = await LobbyAsync(factory, 1, names);
        var players = new List<HubProbe>();
        foreach (var name in names)
        {
            players.Add(await JoinedAsync(factory, quizId, name));
        }
        await using var org = await HubProbe.ConnectAsync(factory, "org");
        await org.InvokeAsync("WatchQuiz", quizId);
        await StartAsync(factory, quizId);
        var started = await players[0].NextAsync("QuestionStarted");
        Assert.False(started.TryGetProperty("correctOptionId", out _)); // never sent before the close
        var questionId = Str(started, "questionId");

        // All 20 answer at once, 3 s into the question; every third player is wrong.
        await factory.AdvanceAsync(TimeSpan.FromSeconds(3));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(n => AnswerAsync(players[n], quizId, questionId, n % 3 == 2 ? "b" : "a")));

        // Expected from the inputs alone: 900 for a correct answer with 12 s of 15 left, 0 for a wrong one;
        // competition ranks: the 14 correct share rank 1, the 6 wrong share rank 15.
        var expectedPoints = Enumerable.Range(0, 20)
            .Select(n => n % 3 == 2 ? 0 : Scoring.Points(true, Limit - TimeSpan.FromSeconds(3), Limit))
            .ToArray();
        Assert.Equal(900, expectedPoints[0]);
        int ExpectedRank(int n) => 1 + expectedPoints.Count(p => p > expectedPoints[n]);
        for (var n = 0; n < 20; n++)
        {
            var closed = await players[n].NextAsync("QuestionClosed");
            Assert.Equal(("a", n % 3 == 2 ? "b" : "a", n % 3 != 2), (Str(closed, "correctOptionId"), Str(closed, "yourOptionId"), closed.GetProperty("correct").GetBoolean()));
            Assert.Equal((expectedPoints[n], expectedPoints[n], ExpectedRank(n)), (Int(closed, "points"), Int(closed, "totalPoints"), Int(closed, "rank")));
            Assert.Equal(10, closed.GetProperty("top10").GetArrayLength());
        }
        await using (var db = factory.CreateDbContext())
        {
            var stored = await db.Players.Where(p => p.QuizId == quizId).ToDictionaryAsync(p => p.DisplayName, p => p.TotalPoints);
            Assert.All(Enumerable.Range(0, 20), n => Assert.Equal(expectedPoints[n], stored[names[n]]));
        }
        var organizerClosed = await org.NextAsync("QuestionClosed");
        Assert.Equal(20, organizerClosed.GetProperty("leaderboard").GetArrayLength());

        await factory.AdvanceAsync(Reveal);
        var lowest = Enumerable.Range(0, 20).Last(n => expectedPoints[n] == 0); // ranked 15, below the top 10
        var finished = await players[lowest].NextAsync("QuizFinished");
        Assert.Equal((15, 0), (Int(finished, "rank"), Int(finished, "totalPoints")));
        Assert.Equal(20, (await org.NextAsync("QuizFinished")).GetProperty("leaderboard").GetArrayLength());
        foreach (var p in players)
        {
            await p.DisposeAsync();
        }
    }

    [Fact]
    public async Task Close_AfterTheRevealTheNextQuestionStartsWithoutAnOrganizerConnected()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 2, "alice", "bob");
        await using var alice = await JoinedAsync(factory, quizId, "alice");
        await using var bob = await JoinedAsync(factory, quizId, "bob");
        await StartAsync(factory, quizId);
        await alice.NextAsync("QuestionStarted");

        await factory.AdvanceAsync(Limit); // nobody answered: the deadline closes it
        var closed = await alice.NextAsync("QuestionClosed");
        await factory.AdvanceAsync(Reveal);
        var next = await bob.NextAsync("QuestionStarted", e => Int(e, "index") == 1);

        Assert.Equal((JsonValueKind.Null, false, 0), (closed.GetProperty("yourOptionId").ValueKind, closed.GetProperty("correct").GetBoolean(), Int(closed, "points")));
        Assert.Equal(2, Int(next, "total"));
        Assert.Equal(4, next.GetProperty("options").GetArrayLength());
        Assert.False(next.TryGetProperty("correctOptionId", out _));
        Assert.True(next.TryGetProperty("serverNow", out _));
    }

    [Fact]
    public async Task Disconnect_MidQuestion_PushesOfflineKeepsThePlayerAndClosesWhenTheRestAnswered()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 3, "alice", "bob", "carol");
        await using var alice = await JoinedAsync(factory, quizId, "alice");
        await using var bob = await JoinedAsync(factory, quizId, "bob");
        var carol = await JoinedAsync(factory, quizId, "carol");
        await StartAsync(factory, quizId);
        var questionId = Str(await alice.NextAsync("QuestionStarted"), "questionId");
        await AnswerAsync(alice, quizId, questionId, "a");
        await AnswerAsync(bob, quizId, questionId, "a");

        await carol.DisposeAsync();

        var offline = await alice.NextAsync("PlayerOnlineChanged");
        Assert.Equal(("carol", false), (Str(offline, "username"), offline.GetProperty("online").GetBoolean()));
        var closed = await alice.NextAsync("QuestionClosed");
        Assert.Contains(closed.GetProperty("top10").EnumerateArray(), e => Str(e, "username") == "carol" && !e.GetProperty("online").GetBoolean());
    }

    [Fact]
    public async Task Disconnect_TheLastOnlinePlayer_FinishesTheQuiz()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 3, "alice");
        var alice = await JoinedAsync(factory, quizId, "alice");
        await using var org = await HubProbe.ConnectAsync(factory, "org");
        await org.InvokeAsync("WatchQuiz", quizId);
        await StartAsync(factory, quizId);

        await alice.DisposeAsync();

        var finished = await org.NextAsync("QuizFinished");
        Assert.Equal(1, finished.GetProperty("leaderboard").GetArrayLength());
        await using var db = factory.CreateDbContext();
        Assert.Equal(SessionState.Finished, (await db.Sessions.SingleAsync(s => s.QuizId == quizId)).State);
    }

    [Fact]
    public async Task JoinQuiz_ASecondQuizOnTheSameConnection_FailsWithInvalidAndTheFirstQuizStillSeesTheDisconnect()
    {
        using var factory = new QuizApiFactory();
        var quizA = await LobbyAsync(factory, 3, "alice");
        var (quizB, _) = await factory.AddLobbyAsync(3, organizer: "org2");
        var alice = await JoinedAsync(factory, quizA, "alice");
        await using var org = await HubProbe.ConnectAsync(factory, "org");
        await org.InvokeAsync("WatchQuiz", quizA);
        await StartAsync(factory, quizA);

        Assert.Equal("Invalid", await alice.ErrorOfAsync("JoinQuiz", quizB));
        await alice.DisposeAsync();

        await org.NextAsync("QuizFinished"); // the only player of A went offline (AC24)
        await using var db = factory.CreateDbContext();
        Assert.Equal(SessionState.Finished, (await db.Sessions.SingleAsync(s => s.QuizId == quizA)).State);
        Assert.Equal(0, await db.Players.CountAsync(p => p.QuizId == quizB));
    }

    [Fact]
    public async Task JoinQuiz_TheSameQuizAgainOnTheSameConnection_IsTheSamePlayer()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 3, "alice");
        await using var alice = await JoinedAsync(factory, quizId, "alice");

        var again = await alice.InvokeAsync("JoinQuiz", quizId.ToLowerInvariant());

        Assert.Equal(1, Int(again, "playerCount"));
    }

    [Fact]
    public async Task WatchQuiz_ByTheOrganizer_ReturnsASnapshotInEveryState()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 1, "alice", "bob");
        await using var alice = await JoinedAsync(factory, quizId, "alice");
        await using var bob = await JoinedAsync(factory, quizId, "bob");
        await using var org = await HubProbe.ConnectAsync(factory, "org");

        var lobby = await org.InvokeAsync("WatchQuiz", quizId);
        await StartAsync(factory, quizId);
        var questionId = Str(await alice.NextAsync("QuestionStarted"), "questionId");
        await AnswerAsync(alice, quizId, questionId, "a");
        await using var reconnected = await HubProbe.ConnectAsync(factory, "org");
        var running = await reconnected.InvokeAsync("WatchQuiz", quizId);
        await AnswerAsync(bob, quizId, questionId, "b");
        await factory.AdvanceAsync(Reveal);
        var finished = await reconnected.InvokeAsync("WatchQuiz", quizId);

        Assert.Equal(("Lobby", 2), (Str(lobby, "state"), lobby.GetProperty("players").GetArrayLength()));
        Assert.Equal(("Running", questionId), (Str(running, "state"), Str(running.GetProperty("currentQuestion"), "questionId")));
        Assert.Equal((1, 2), (Int(running.GetProperty("answersCount"), "answered"), Int(running.GetProperty("answersCount"), "eligible")));
        Assert.All(running.GetProperty("leaderboard").EnumerateArray(), e => Assert.Equal(0, Int(e, "totalPoints"))); // open question: hidden
        Assert.Equal("Finished", Str(finished, "state"));
        Assert.Equal("alice", Str(finished.GetProperty("leaderboard")[0], "username"));
    }

    [Fact]
    public async Task WatchQuiz_ByAnotherUserOrForAnUnknownQuiz_Fails()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 1, "alice");
        await using var alice = await HubProbe.ConnectAsync(factory, "alice");

        Assert.Equal("NotOrganizer", await alice.ErrorOfAsync("WatchQuiz", quizId));
        Assert.Equal("QuizNotFound", await alice.ErrorOfAsync("WatchQuiz", "ZZZ999"));
    }

    [Fact]
    public async Task JoinQuiz_FailingOnTheOrganizersWatchingConnection_KeepsItInTheSessionGroup()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 1);
        for (var i = 0; i < 20; i++)
        {
            await factory.JoinAsync(quizId, await factory.AddUserAsync($"p{i:D2}"), $"p{i:D2}");
        }
        await using var org = await HubProbe.ConnectAsync(factory, "org");
        await org.InvokeAsync("WatchQuiz", quizId);

        Assert.Equal("SessionFull", await org.ErrorOfAsync("JoinQuiz", quizId)); // the organizer takes a seat too
        await StartAsync(factory, quizId);

        await org.NextAsync("QuestionStarted"); // still in the session group
    }

    [Fact]
    public async Task WatchQuiz_ByAnotherUser_DoesNotLeaveTheConnectionInTheGroups()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 1, "alice", "bob");
        await using var alice = await HubProbe.ConnectAsync(factory, "alice");

        Assert.Equal("NotOrganizer", await alice.ErrorOfAsync("WatchQuiz", quizId));
        await using var bob = await JoinedAsync(factory, quizId, "bob");

        Assert.False(await alice.ReceivesAsync("PlayerJoined", TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Rejoin_AfterADisconnect_KeepsThePointsAndComesBackOnline()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 2, "alice", "bob");
        var alice = await JoinedAsync(factory, quizId, "alice");
        await using var bob = await JoinedAsync(factory, quizId, "bob");
        await StartAsync(factory, quizId);
        var questionId = Str(await bob.NextAsync("QuestionStarted"), "questionId");
        await AnswerAsync(alice, quizId, questionId, "a"); // 1000 points
        await AnswerAsync(bob, quizId, questionId, "b");   // closes q1
        await alice.DisposeAsync();
        await bob.NextAsync("PlayerOnlineChanged", e => !e.GetProperty("online").GetBoolean());

        await using var back = await HubProbe.ConnectAsync(factory, "alice");
        var result = await back.InvokeAsync("JoinQuiz", quizId);

        Assert.Equal(1000, Int(result.GetProperty("you"), "totalPoints"));
        Assert.Equal(1, Int(result.GetProperty("you"), "rank"));
        var online = await bob.NextAsync("PlayerOnlineChanged", e => e.GetProperty("online").GetBoolean());
        Assert.Equal("alice", Str(online, "username"));
        await using var db = factory.CreateDbContext();
        Assert.Equal(2, await db.Players.CountAsync(p => p.QuizId == quizId));
    }

    [Fact]
    public async Task UnexpectedFailure_ReturnsOnlyServerErrorAndTheNextCallWorks()
    {
        using var factory = new QuizApiFactory();
        var quizId = await LobbyAsync(factory, 1, "alice");
        await using var alice = await HubProbe.ConnectAsync(factory, "alice");
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE Sessions SET Phase = 'Bogus' WHERE QuizId = {quizId}"); // unreadable row
        }

        Assert.Equal("ServerError", await alice.ErrorOfAsync("JoinQuiz", quizId)); // the code only, no exception text

        await using (var db = factory.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE Sessions SET Phase = 'None' WHERE QuizId = {quizId}");
        }
        var result = await alice.InvokeAsync("JoinQuiz", quizId);
        Assert.Equal("alice", Str(result, "username"));
    }

    [Fact]
    public async Task Metrics_EveryInstrumentIsRecorded()
    {
        using var factory = new QuizApiFactory();
        var meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        var seen = new HashSet<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == QuizMetrics.MeterName && instrument.Meter.Scope == meterFactory)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((i, _, _, _) => { lock (seen) { seen.Add(i.Name); } });
        listener.SetMeasurementEventCallback<long>((i, _, _, _) => { lock (seen) { seen.Add(i.Name); } });
        listener.SetMeasurementEventCallback<double>((i, _, _, _) => { lock (seen) { seen.Add(i.Name); } });
        listener.Start();
        var quizId = await LobbyAsync(factory, 1, "alice");
        await using var alice = await JoinedAsync(factory, quizId, "alice");
        await StartAsync(factory, quizId);
        var questionId = Str(await alice.NextAsync("QuestionStarted"), "questionId");

        await alice.ErrorOfAsync("SubmitAnswer", quizId, questionId, "z", Guid.NewGuid());
        await AnswerAsync(alice, quizId, questionId, "a"); // closes the question
        await alice.NextAsync("QuestionClosed");

        Assert.Superset(new HashSet<string>
        {
            "quiz.sessions.live", "quiz.connections", "quiz.answers.accepted", "quiz.answers.rejected",
            "quiz.actor.message.duration", "quiz.question.close_push.duration",
        }, seen);
    }
}
