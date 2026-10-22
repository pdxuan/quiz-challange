using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quiz.Server.Domain;
using Quiz.Server.Runtime;
using Quiz.Server.Tests.TestHost;

namespace Quiz.Server.Tests.Runtime;

public class SessionActorTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Reveal = TimeSpan.FromSeconds(5);

    private static async Task<(string QuizId, Guid Organizer, Guid Alice, Guid Bob)> RunningAsync(QuizApiFactory factory, int questions = 3)
    {
        var (quizId, organizer) = await factory.AddLobbyAsync(questions);
        var alice = await factory.AddUserAsync("alice");
        var bob = await factory.AddUserAsync("bob");
        await factory.JoinAsync(quizId, alice, "alice");
        await factory.JoinAsync(quizId, bob, "bob");
        await factory.Registry.AskAsync(quizId, (s, call) => { s.Start(organizer, call.Now, call.Rules); return 0; });
        return (quizId, organizer, alice, bob);
    }

    private static async Task<Session> LoadAsync(QuizApiFactory factory, string quizId)
    {
        await using var db = factory.CreateDbContext();
        return await db.Sessions.Include(s => s.QuizSet).ThenInclude(q => q.Questions).Include(s => s.Players).Include(s => s.Answers)
            .AsNoTracking().SingleAsync(s => s.QuizId == quizId);
    }

    [Fact]
    public async Task Tick_AtTheDeadline_ClosesTheQuestionThenStartsTheNextAfterTheReveal()
    {
        using var factory = new QuizApiFactory();
        var (quizId, _, _, _) = await RunningAsync(factory);

        await factory.AdvanceAsync(Limit);
        var closed = await LoadAsync(factory, quizId);
        await factory.AdvanceAsync(Reveal);
        var next = await LoadAsync(factory, quizId);

        Assert.Equal((0, SessionPhase.Reveal), (closed.CurrentIndex, closed.Phase));
        Assert.Equal((1, SessionPhase.Question), (next.CurrentIndex, next.Phase));
        Assert.Contains(factory.Events.For(quizId), e => e is QuestionClosed { QuestionId: var q } && q.EndsWith("-q1"));
        Assert.Contains(factory.Events.For(quizId), e => e is QuestionStarted { Index: 1 });
    }

    [Fact]
    public async Task Tick_BeforeTheDeadline_ChangesNothing()
    {
        using var factory = new QuizApiFactory();
        var (quizId, _, _, _) = await RunningAsync(factory);

        await factory.AdvanceAsync(Limit - TimeSpan.FromMilliseconds(200));

        Assert.Equal(SessionPhase.Question, (await LoadAsync(factory, quizId)).Phase);
    }

    [Fact]
    public async Task Ask_AfterTheLastQuestion_FinishesAndTheSessionStaysReadable()
    {
        using var factory = new QuizApiFactory();
        var (quizId, _, _, _) = await RunningAsync(factory, questions: 1);

        await factory.AdvanceAsync(Limit);
        await factory.AdvanceAsync(Reveal);

        var stored = await LoadAsync(factory, quizId);
        Assert.Equal(SessionState.Finished, stored.State);
        Assert.Contains(factory.Events.For(quizId), e => e is QuizFinished);
        var error = await Assert.ThrowsAsync<SessionException>(() => factory.JoinAsync(quizId, Guid.NewGuid(), "late"));
        Assert.Equal(SessionError.QuizFinished, error.Error);
    }

    [Fact]
    public async Task Ask_UnknownQuiz_IsQuizNotFound()
    {
        using var factory = new QuizApiFactory();

        var error = await Assert.ThrowsAsync<SessionException>(() => factory.Registry.AskAsync("ZZZ999", (s, _) => s.State));

        Assert.Equal(SessionError.QuizNotFound, error.Error);
    }

    [Fact]
    public async Task Ask_RuleFailure_ChangesNothingAndReturnsItsCode()
    {
        using var factory = new QuizApiFactory();
        var (quizId, _, alice, _) = await RunningAsync(factory);

        var error = await Assert.ThrowsAsync<SessionException>(() => factory.Registry.AskAsync(quizId, (s, call) =>
            s.SubmitAnswer(alice, "wrong-question", "a", Guid.NewGuid(), call.Now, call.Now, call.Rules)));

        Assert.Equal(SessionError.NotCurrentQuestion, error.Error);
        Assert.Empty((await LoadAsync(factory, quizId)).Answers);
    }

    [Fact]
    public async Task Ask_FailingSave_RollsBackFailsOnlyThatCallLogsTheQuizAndTheNextCallWorks()
    {
        using var factory = new QuizApiFactory();
        var (quizId, _, alice, _) = await RunningAsync(factory);
        var carol = await factory.AddUserAsync("carol");

        // A valid join plus a row that breaks a foreign key: the save fails, so the join must not stick.
        var failure = await Assert.ThrowsAsync<SessionActorException>(() => factory.Registry.AskAsync(quizId, (s, call) =>
        {
            s.Join(carol, "carol", "conn-carol", call.Now, call.Rules);
            s.Answers.Add(new Answer(quizId, alice, s.CurrentQuestion!.Id, "no-such-option", Guid.NewGuid(), call.Now, 0));
            return 0;
        }));

        Assert.DoesNotContain("no-such-option", failure.Message);
        Assert.DoesNotContain((await LoadAsync(factory, quizId)).Players, p => p.UserId == carol);
        var logged = Assert.Single(factory.Logs.Entries, e => e.Level == LogLevel.Error && e.Category.EndsWith(nameof(SessionActor)));
        Assert.Equal(quizId, logged.Scope("quizId"));
        Assert.NotNull(logged.Exception);

        await factory.JoinAsync(quizId, carol, "carol");
        Assert.Contains((await LoadAsync(factory, quizId)).Players, p => p.UserId == carol);
    }

    [Fact]
    public async Task Ask_TwoAnswersFromOnePlayerAtOnce_StoresExactlyOne()
    {
        using var factory = new QuizApiFactory();
        var (quizId, _, alice, _) = await RunningAsync(factory);
        var now = factory.Time.GetUtcNow();
        Task<AnswerOutcome> Submit(string option) => factory.Registry.AskAsync(quizId, (s, call) =>
            s.SubmitAnswer(alice, s.CurrentQuestion!.Id, option, Guid.NewGuid(), now, call.Now, call.Rules));

        var results = await Task.WhenAll(Submit("a").ContinueWith(t => t), Submit("b").ContinueWith(t => t));

        Assert.Equal(1, results.Count(t => t.IsCompletedSuccessfully));
        var rejected = Assert.Single(results, t => t.IsFaulted).Exception!.InnerException;
        Assert.Equal(SessionError.AlreadyAnswered, Assert.IsType<SessionException>(rejected).Error);
        Assert.Single((await LoadAsync(factory, quizId)).Answers);
    }

    [Fact]
    public async Task Ask_ManyConcurrentCallsOnTwoSessions_KeepEachSessionConsistent()
    {
        using var factory = new QuizApiFactory();
        var (first, firstOrganizer) = await factory.AddLobbyAsync(organizer: "org1");
        var (second, _) = await factory.AddLobbyAsync(organizer: "org2");
        var users = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            users.Add(await factory.AddUserAsync($"player{i:D2}"));
        }

        await Task.WhenAll(users.SelectMany(u => new[]
        {
            factory.JoinAsync(first, u, $"p-{u:N}"),
            factory.JoinAsync(second, u, $"p-{u:N}"),
        }));

        Assert.Equal(20, (await LoadAsync(factory, first)).Players.Count);
        Assert.Equal(20, (await LoadAsync(factory, second)).Players.Count);
        Assert.Equal(firstOrganizer, (await LoadAsync(factory, first)).OrganizerUserId);
    }

    [Fact]
    public async Task Restart_ResumesARunningSession_ClosesTheOverdueQuestionAndStartsTheNextWhenAPlayerRejoins()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiz-tests", Guid.NewGuid().ToString("N"));
        string quizId;
        Guid alice;
        using (var before = new QuizApiFactory { Root = root, DeleteFilesOnDispose = false })
        {
            (quizId, _, alice, _) = await RunningAsync(before);
            await AnswerNowAsync(before, quizId, alice, "a"); // alice: 1000 points on q1, then the server goes down
        }

        using var after = new QuizApiFactory { Root = root, Time = new(QuizApiFactory.StartTime.AddMinutes(2)) };
        _ = after.Services; // starts the host: recovery marks everyone offline and restarts the actor
        await after.AdvanceAsync(TimeSpan.FromMilliseconds(100));
        var overdueClosed = await LoadAsync(after, quizId);
        await after.JoinAsync(quizId, alice, "alice"); // alice comes back during the reveal
        await after.AdvanceAsync(Reveal);
        var resumed = await LoadAsync(after, quizId);

        Assert.Equal((SessionState.Running, 0, SessionPhase.Reveal), (overdueClosed.State, overdueClosed.CurrentIndex, overdueClosed.Phase));
        Assert.All(overdueClosed.Players, p => Assert.False(p.Online));
        Assert.Equal((SessionState.Running, 1, SessionPhase.Question), (resumed.State, resumed.CurrentIndex, resumed.Phase));
        Assert.Equal(1000, resumed.Players.Single(p => p.UserId == alice).TotalPoints);
        Assert.False(resumed.Players.Single(p => p.UserId != alice).Online);
    }

    [Fact]
    public async Task Restart_NobodyRejoins_FinishesAtTheEndOfTheReveal()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiz-tests", Guid.NewGuid().ToString("N"));
        string quizId;
        using (var before = new QuizApiFactory { Root = root, DeleteFilesOnDispose = false })
        {
            (quizId, _, _, _) = await RunningAsync(before);
        }

        using var after = new QuizApiFactory { Root = root, Time = new(QuizApiFactory.StartTime.AddMinutes(2)) };
        _ = after.Services;
        await after.AdvanceAsync(TimeSpan.FromMilliseconds(100));
        await after.AdvanceAsync(Reveal);

        Assert.Equal(SessionState.Finished, (await LoadAsync(after, quizId)).State);
    }

    [Fact]
    public async Task Recovery_MarksLobbyPlayersOfflineToo()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiz-tests", Guid.NewGuid().ToString("N"));
        string quizId;
        using (var before = new QuizApiFactory { Root = root, DeleteFilesOnDispose = false })
        {
            (quizId, _) = await before.AddLobbyAsync();
            await before.JoinAsync(quizId, await before.AddUserAsync("alice"), "alice");
        }

        using var after = new QuizApiFactory { Root = root };
        _ = after.Services;
        await after.Registry.FlushAllAsync();

        var stored = await LoadAsync(after, quizId);
        Assert.Equal(SessionState.Lobby, stored.State);
        Assert.False(Assert.Single(stored.Players).Online);
    }

    [Fact]
    public async Task Recovery_ABrokenSession_IsLoggedAndSkippedAndTheServerStillStarts()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiz-tests", Guid.NewGuid().ToString("N"));
        string broken, healthy;
        using (var before = new QuizApiFactory { Root = root, DeleteFilesOnDispose = false })
        {
            (broken, _) = await before.AddLobbyAsync(organizer: "org1");
            (healthy, _) = await before.AddLobbyAsync(organizer: "org2");
            await before.JoinAsync(healthy, await before.AddUserAsync("alice"), "alice");
            await using var db = before.CreateDbContext();
            await db.Database.ExecuteSqlAsync($"UPDATE Sessions SET Phase = 'Bogus' WHERE QuizId = {broken}"); // unreadable row
        }

        using var after = new QuizApiFactory { Root = root };
        var health = await after.CreateClient().GetAsync("/health"); // the host started

        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
        Assert.False(Assert.Single((await LoadAsync(after, healthy)).Players).Online);
        var warning = Assert.Single(after.Logs.Entries, e => e.Level == LogLevel.Warning && e.Category.EndsWith(nameof(SessionRecoveryService)));
        Assert.Contains(broken, warning.Message);
    }

    [Fact]
    public async Task Lobby_AfterACall_TheActorStops()
    {
        using var factory = new QuizApiFactory();
        var (quizId, _) = await factory.AddLobbyAsync();

        await factory.JoinAsync(quizId, await factory.AddUserAsync("alice"), "alice");
        await factory.Registry.FlushAllAsync();

        Assert.Equal(0, factory.Registry.LiveActorCount); // a Lobby has no deadlines, so nothing needs to tick
    }

    [Fact]
    public async Task Running_KeepsItsActorTicking()
    {
        using var factory = new QuizApiFactory();
        await RunningAsync(factory);

        await factory.Registry.FlushAllAsync();

        Assert.Equal(1, factory.Registry.LiveActorCount);
    }

    [Fact]
    public async Task Recovery_ABrokenSession_StopsItsActorAfterThreeFailuresInARow()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiz-tests", Guid.NewGuid().ToString("N"));
        string broken;
        using (var before = new QuizApiFactory { Root = root, DeleteFilesOnDispose = false })
        {
            broken = (await RunningAsync(before)).QuizId;
            await using var db = before.CreateDbContext();
            await db.Database.ExecuteSqlAsync($"UPDATE Sessions SET Phase = 'Bogus' WHERE QuizId = {broken}");
        }

        using var after = new QuizApiFactory { Root = root };
        _ = after.Services; // recovery fails once
        for (var i = 0; i < 20; i++)
        {
            await after.AdvanceAsync(TimeSpan.FromMilliseconds(100)); // each tick would fail again
        }

        var errors = after.Logs.Entries.Count(e => e.Level == LogLevel.Error && e.Category.EndsWith(nameof(SessionActor)) && Equals(e.Scope("quizId"), broken));
        Assert.Equal(3, errors);
        Assert.Equal(0, after.Registry.LiveActorCount);
    }

    [Fact]
    public async Task Restart_DuringAnOverdueReveal_LeavesARevealToRejoinThenContinues()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiz-tests", Guid.NewGuid().ToString("N"));
        string quizId;
        Guid alice, bob;
        using (var before = new QuizApiFactory { Root = root, DeleteFilesOnDispose = false })
        {
            (quizId, _, alice, bob) = await RunningAsync(before);
            await AnswerNowAsync(before, quizId, alice, "a");
            await AnswerNowAsync(before, quizId, bob, "b"); // all answered: the reveal starts, then the server goes down
        }

        using var after = new QuizApiFactory { Root = root, Time = new(QuizApiFactory.StartTime.AddMinutes(2)) };
        _ = after.Services;
        await after.AdvanceAsync(TimeSpan.FromMilliseconds(100));
        var stillRevealing = await LoadAsync(after, quizId);
        await after.JoinAsync(quizId, alice, "alice");
        await after.AdvanceAsync(Reveal);
        var resumed = await LoadAsync(after, quizId);

        Assert.Equal((SessionState.Running, SessionPhase.Reveal), (stillRevealing.State, stillRevealing.Phase));
        Assert.Equal((SessionState.Running, 1, SessionPhase.Question), (resumed.State, resumed.CurrentIndex, resumed.Phase));
    }

    private static Task AnswerNowAsync(QuizApiFactory factory, string quizId, Guid userId, string optionId)
    {
        var now = factory.Time.GetUtcNow();
        return factory.Registry.AskAsync(quizId, (s, call) =>
            s.SubmitAnswer(userId, s.CurrentQuestion!.Id, optionId, Guid.NewGuid(), now, call.Now, call.Rules));
    }
}
