using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quiz.Server.Data;
using Quiz.Server.Domain;
using Quiz.Server.Tests.TestHost;

namespace Quiz.Server.Tests.Data;

public class SchemaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly SessionRules Rules = new(20, TimeSpan.FromSeconds(5));

    private static async Task<List<string>> QueryAsync(QuizApiFactory factory, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={factory.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetValue(0).ToString()!);
        }
        return rows;
    }

    [Fact]
    public async Task Startup_MigratesEveryTable()
    {
        using var factory = new QuizApiFactory();
        _ = factory.Services; // starts the host, which migrates

        var tables = await QueryAsync(factory, "SELECT name FROM sqlite_master WHERE type = 'table'");

        Assert.Superset(
            new HashSet<string> { "Users", "QuizSets", "Questions", "Options", "Sessions", "Players", "Answers", "__EFMigrationsHistory" },
            tables.ToHashSet());
    }

    [Theory]
    [InlineData("Answers", "QuizId,UserId,QuestionId")]
    [InlineData("Players", "QuizId,UserId")]
    [InlineData("Options", "QuestionId,OptionId")]
    [InlineData("Sessions", "QuizId")]
    [InlineData("Users", "UserId")]
    public async Task Startup_CreatesThePrimaryKeys(string table, string expectedKey)
    {
        using var factory = new QuizApiFactory();
        _ = factory.Services;

        var key = await QueryAsync(factory, $"SELECT name FROM pragma_table_info('{table}') WHERE pk > 0 ORDER BY pk");

        Assert.Equal(expectedKey, string.Join(",", key));
    }

    [Fact]
    public async Task Startup_SetsWalJournalMode()
    {
        using var factory = new QuizApiFactory();
        _ = factory.Services;

        Assert.Equal(["wal"], await QueryAsync(factory, "PRAGMA journal_mode"));
    }

    [Fact]
    public void Model_MatchesTheMigrations()
    {
        using var factory = new QuizApiFactory();
        using var db = factory.CreateDbContext();

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(db.Database.GetPendingMigrations());
    }

    [Fact]
    public async Task Session_RoundTrip_KeepsEveryField()
    {
        using var factory = new QuizApiFactory();
        var set = await factory.AddQuizSetAsync();
        var organizer = await factory.AddUserAsync("org");
        var alice = await factory.AddUserAsync("alice");
        var bob = await factory.AddUserAsync("bob");
        var submission = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            var quizSet = await db.QuizSets.Include(s => s.Questions).ThenInclude(q => q.Options).SingleAsync();
            var session = Session.Create("ABC234", quizSet, organizer, T0);
            session.Join(alice, "alice", "conn-a", T0, Rules);
            session.Join(bob, "bob", "conn-b", T0, Rules);
            session.Start(organizer, T0.AddSeconds(1), Rules);
            session.SubmitAnswer(alice, "set-1-q1", "a", submission, T0.AddSeconds(4), T0.AddSeconds(4), Rules);
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
        }

        await using var read = factory.CreateDbContext();
        var loaded = await read.Sessions
            .Include(s => s.QuizSet).ThenInclude(q => q.Questions).ThenInclude(q => q.Options)
            .Include(s => s.Players)
            .Include(s => s.Answers)
            .SingleAsync(s => s.QuizId == "ABC234");

        Assert.Equal(("set-1", organizer, SessionState.Running, 0, SessionPhase.Question),
            (loaded.QuizSetId, loaded.OrganizerUserId, loaded.State, loaded.CurrentIndex, loaded.Phase));
        Assert.Equal((T0, T0.AddSeconds(1), T0.AddSeconds(16), (DateTimeOffset?)null),
            (loaded.CreatedAt, loaded.QuestionStartedAt!.Value, loaded.PhaseEndsAt!.Value, loaded.FinishedAt));
        Assert.Equal("set-1-q1", loaded.CurrentQuestion!.Id);
        Assert.Equal(4, loaded.CurrentQuestion.Options.Count);

        var a = loaded.Players.Single(p => p.UserId == alice);
        Assert.Equal(("alice", "conn-a", true, T0, 900, T0.AddSeconds(4)),
            (a.DisplayName, a.ConnectionId, a.Online, a.JoinedAt, a.TotalPoints, a.ReachedAt));
        var answer = Assert.Single(loaded.Answers);
        Assert.Equal((alice, "set-1-q1", "a", submission, T0.AddSeconds(4), 900),
            (answer.UserId, answer.QuestionId, answer.OptionId, answer.SubmissionId, answer.ReceivedAt, answer.Points));
        Assert.Equal(set.Questions.Count, loaded.QuizSet.Questions.Count);
    }

    [Fact]
    public async Task Answers_DuplicateKey_IsRejected()
    {
        using var factory = new QuizApiFactory();
        await SeedRunningSessionAsync(factory);
        var alice = await UserIdAsync(factory, "alice");

        await using (var first = factory.CreateDbContext())
        {
            first.Answers.Add(new Answer("ABC234", alice, "set-1-q1", "a", Guid.NewGuid(), T0, 900));
            await first.SaveChangesAsync();
        }

        // A separate context, so the database's primary key (not EF's tracker) has to stop it.
        await using var second = factory.CreateDbContext();
        second.Answers.Add(new Answer("ABC234", alice, "set-1-q1", "b", Guid.NewGuid(), T0, 0));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Answers_OptionOfAnotherQuestion_IsRejected()
    {
        using var factory = new QuizApiFactory();
        await SeedRunningSessionAsync(factory);
        var alice = await UserIdAsync(factory, "alice");

        await using var db = factory.CreateDbContext();
        db.Answers.Add(new Answer("ABC234", alice, "set-1-q1", "z", Guid.NewGuid(), T0, 0));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Answers_ReferenceOnlyTheirOptionPlayerAndSession()
    {
        using var factory = new QuizApiFactory();
        _ = factory.Services;

        var referenced = await QueryAsync(factory, "SELECT DISTINCT \"table\" FROM pragma_foreign_key_list('Answers') ORDER BY 1");

        Assert.Equal(["Options", "Players", "Sessions"], referenced); // the option FK already implies the question
    }

    [Fact]
    public async Task DateTimeOffsets_AreStoredAsUtc()
    {
        using var factory = new QuizApiFactory();
        await factory.AddQuizSetAsync();
        var organizer = await factory.AddUserAsync("org");
        var createdInHanoi = new DateTimeOffset(2026, 1, 1, 16, 0, 0, TimeSpan.FromHours(7)); // = 09:00 UTC
        await using (var db = factory.CreateDbContext())
        {
            var quizSet = await db.QuizSets.Include(s => s.Questions).ThenInclude(q => q.Options).SingleAsync();
            db.Sessions.Add(Session.Create("ABC234", quizSet, organizer, createdInHanoi));
            await db.SaveChangesAsync();
        }

        var stored = await QueryAsync(factory, "SELECT CreatedAt FROM Sessions");
        await using var read = factory.CreateDbContext();
        var loaded = await read.Sessions.SingleAsync();

        Assert.EndsWith("+00:00", Assert.Single(stored));
        Assert.Equal((T0, TimeSpan.Zero), (loaded.CreatedAt, loaded.CreatedAt.Offset));
    }

    [Fact]
    public void ConnectionString_HasAnExplicitBusyTimeout()
    {
        using var factory = new QuizApiFactory();
        using var db = factory.CreateDbContext();

        Assert.Equal(5, new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DefaultTimeout);
    }

    [Fact]
    public async Task Users_WithSessions_CannotBeDeleted()
    {
        using var factory = new QuizApiFactory();
        await SeedRunningSessionAsync(factory);

        await using var db = factory.CreateDbContext();
        db.Users.Remove(await db.Users.SingleAsync(u => u.Username == "org"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task QuizSets_UsedBySessions_CannotBeDeleted()
    {
        using var factory = new QuizApiFactory();
        await SeedRunningSessionAsync(factory);

        await using var db = factory.CreateDbContext();
        db.QuizSets.Remove(await db.QuizSets.SingleAsync());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Users_UsernameIsUniqueIgnoringCase()
    {
        using var factory = new QuizApiFactory();
        await factory.AddUserAsync("Alice");

        await Assert.ThrowsAsync<DbUpdateException>(() => factory.AddUserAsync("alice"));
    }

    private static async Task SeedRunningSessionAsync(QuizApiFactory factory)
    {
        await factory.AddQuizSetAsync();
        var organizer = await factory.AddUserAsync("org");
        var alice = await factory.AddUserAsync("alice");
        await using var db = factory.CreateDbContext();
        var quizSet = await db.QuizSets.Include(s => s.Questions).ThenInclude(q => q.Options).SingleAsync();
        var session = Session.Create("ABC234", quizSet, organizer, T0);
        session.Join(alice, "alice", "conn-a", T0, Rules);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> UserIdAsync(QuizApiFactory factory, string username)
    {
        await using var db = factory.CreateDbContext();
        return (await db.Users.SingleAsync(u => u.Username == username)).UserId;
    }
}
