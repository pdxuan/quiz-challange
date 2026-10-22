// AI-assisted: Claude Code (Claude Opus 5.5), slice S6; reviewed by a reviewer agent and a human; verified by EndToEndTests (expected values from the inputs) (docs/ai-collab/s6.md).
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quiz.Server.Domain;
using Quiz.Server.Tests.TestHost;

namespace Quiz.Server.Tests.EndToEnd;

/// <summary>What one player does on one question: answer at a time after the start, answer late (after the close), or nothing.</summary>
internal sealed record PlannedAnswer(string Player, string OptionId, TimeSpan? At, bool Late);

/// <summary>A real hub client that also keeps every event it received, in order, for the cross-session checks.</summary>
internal sealed class RecordingClient : IAsyncDisposable
{
    private RecordingClient(HubProbe probe) => Probe = probe;

    public HubProbe Probe { get; }

    public ConcurrentQueue<(string Name, JsonElement Payload)> Received { get; } = new();

    public static async Task<RecordingClient> ConnectAsync(QuizApiFactory factory, string username)
    {
        var client = new RecordingClient(await HubProbe.ConnectAsync(factory, username));
        foreach (var name in HubProbe.EventNames)
        {
            client.Probe.Connection.On<JsonElement>(name, payload => client.Received.Enqueue((name, payload)));
        }
        return client;
    }

    public ValueTask DisposeAsync() => Probe.DisposeAsync();
}

/// <summary>
/// One quiz session driven end to end through the public boundary: the organizer logs in, creates
/// the session over REST and watches it over the hub; every player logs in and joins over the hub.
/// Answers go through <c>SubmitAnswer</c>; the clock is moved by the caller.
/// </summary>
internal sealed class E2ESession : IAsyncDisposable
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan Reveal = TimeSpan.FromSeconds(5);

    private readonly QuizApiFactory _factory;
    private readonly List<RecordingClient> _retired = [];

    private E2ESession(QuizApiFactory factory, string prefix, QuizSet set, IReadOnlyList<string> names)
    {
        _factory = factory;
        Prefix = prefix;
        Set = set;
        Names = names;
    }

    public string Prefix { get; }
    public QuizSet Set { get; }
    public IReadOnlyList<string> Names { get; }
    public string QuizId { get; private set; } = "";
    public HttpClient OrganizerHttp { get; private set; } = null!;
    public RecordingClient Organizer { get; private set; } = null!;
    public Dictionary<string, RecordingClient> Players { get; } = [];

    /// <summary>Answers the server accepted: who, which question, which option, how long after the start.</summary>
    public ConcurrentBag<(string Player, string QuestionId, string OptionId, TimeSpan At)> Accepted { get; } = [];

    public int LateRejected;

    private IEnumerable<RecordingClient> AllClients => Players.Values.Concat(_retired).Append(Organizer);

    public static async Task<E2ESession> OpenAsync(QuizApiFactory factory, string prefix, int players, int questions)
    {
        var set = await factory.AddQuizSetAsync($"{prefix}-set", questions, (int)Limit.TotalSeconds);
        var names = Enumerable.Range(1, players).Select(i => $"{prefix}-p{i:D2}").ToList();
        var organizer = $"{prefix}-org";
        await factory.AddUserAsync(organizer);
        foreach (var name in names)
        {
            await factory.AddUserAsync(name);
        }

        var session = new E2ESession(factory, prefix, set, names);
        session.OrganizerHttp = await factory.LoginAsync(organizer);
        var created = await session.OrganizerHttp.PostAsJsonAsync("/api/sessions", new { quizSetId = set.Id });
        created.EnsureSuccessStatusCode();
        session.QuizId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quizId").GetString()!;

        session.Organizer = await RecordingClient.ConnectAsync(factory, organizer);
        await session.Organizer.Probe.InvokeAsync("WatchQuiz", session.QuizId);

        var joined = await Task.WhenAll(names.Select(async name =>
        {
            var client = await RecordingClient.ConnectAsync(factory, name);
            var result = await client.Probe.InvokeAsync("JoinQuiz", session.QuizId);
            Assert.Equal(name, result.GetProperty("username").GetString());
            return (name, client);
        }));
        foreach (var (name, client) in joined)
        {
            session.Players[name] = client;
        }
        return session;
    }

    public async Task StartAsync()
    {
        var response = await OrganizerHttp.PostAsync($"/api/sessions/{QuizId}/start", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Waits for question <paramref name="index"/> on every client and returns its id.</summary>
    public async Task<string> QuestionStartedAsync(int index)
    {
        var expected = Set.Ordered[index].Id;
        var started = await Task.WhenAll(Players.Values.Append(Organizer).Select(c => c.Probe.NextAsync("QuestionStarted")));
        Assert.All(started, e =>
        {
            Assert.Equal((expected, index), (e.GetProperty("questionId").GetString(), e.GetProperty("index").GetInt32()));
            Assert.False(e.TryGetProperty("correctOptionId", out _));
        });
        return expected;
    }

    /// <summary>Plans each player's move on one question with the caller's seeded random source.</summary>
    public List<PlannedAnswer> Plan(Random random, int index, ISet<string> mustAnswer)
    {
        var forcedLate = Names[index % Names.Count]; // someone is always late, so the deadline (not an early close) ends every question
        return Names.Select(name =>
        {
            var option = "abcd"[random.Next(4)].ToString();
            var roll = random.NextDouble();
            var at = TimeSpan.FromMilliseconds(random.Next(0, 150) * 100);
            if (!mustAnswer.Contains(name) && (name == forcedLate || roll < 0.1))
            {
                return new PlannedAnswer(name, option, null, Late: true);
            }
            return !mustAnswer.Contains(name) && roll < 0.15
                ? new PlannedAnswer(name, option, null, Late: false)
                : new PlannedAnswer(name, option, at, Late: false);
        }).ToList();
    }

    public async Task AnswerAsync(string questionId, IEnumerable<PlannedAnswer> answers, TimeSpan at)
    {
        await Task.WhenAll(answers.Select(async a =>
        {
            var accepted = await Players[a.Player].Probe.InvokeAsync("SubmitAnswer", QuizId, questionId, a.OptionId, Guid.NewGuid());
            Assert.Equal(questionId, accepted.GetProperty("questionId").GetString());
            Accepted.Add((a.Player, questionId, a.OptionId, at));
        }));
    }

    public async Task AnswerLateAsync(string questionId, IEnumerable<PlannedAnswer> answers)
    {
        await Task.WhenAll(answers.Select(async a =>
        {
            Assert.Equal("QuestionClosed", await Players[a.Player].Probe.ErrorOfAsync("SubmitAnswer", QuizId, questionId, a.OptionId, Guid.NewGuid()));
            Interlocked.Increment(ref LateRejected);
        }));
    }

    /// <summary>Every player gets their own close of <paramref name="questionId"/>; a player with no accepted answer gets 0 points.</summary>
    public async Task QuestionClosedAsync(string questionId)
    {
        var answered = Accepted.Where(a => a.QuestionId == questionId).ToDictionary(a => a.Player, a => a.OptionId);
        await Task.WhenAll(Players.Select(async p =>
        {
            var closed = await p.Value.Probe.NextAsync("QuestionClosed");
            Assert.Equal(questionId, closed.GetProperty("questionId").GetString());
            var yours = closed.GetProperty("yourOptionId");
            Assert.Equal(answered.GetValueOrDefault(p.Key), yours.ValueKind == JsonValueKind.Null ? null : yours.GetString());
        }));
        Assert.Equal(questionId, (await Organizer.Probe.NextAsync("QuestionClosed")).GetProperty("questionId").GetString());
    }

    /// <summary>Drops a player's connection mid-question, then rejoins on a new connection as the same player.</summary>
    public async Task DisconnectAndRejoinAsync(string name, string questionId)
    {
        var old = Players[name];
        await old.DisposeAsync();
        _retired.Add(old);
        await Organizer.Probe.NextAsync("PlayerOnlineChanged", e => e.GetProperty("username").GetString() == name && !e.GetProperty("online").GetBoolean());

        var client = await RecordingClient.ConnectAsync(_factory, name);
        var result = await client.Probe.InvokeAsync("JoinQuiz", QuizId);
        Players[name] = client;

        Assert.Equal((Names.Count, true), (result.GetProperty("playerCount").GetInt32(), result.GetProperty("canAnswerCurrent").GetBoolean()));
        Assert.Equal(questionId, result.GetProperty("currentQuestion").GetProperty("questionId").GetString());
        await Organizer.Probe.NextAsync("PlayerOnlineChanged", e => e.GetProperty("username").GetString() == name && e.GetProperty("online").GetBoolean());
    }

    /// <summary>
    /// Every player's total and rank in <c>QuizFinished</c> (and the organizer's full board) equal
    /// the values recomputed from the <c>Answers</c> rows, and those rows are exactly the accepted
    /// answers, each scored from the time it was sent.
    /// </summary>
    public async Task VerifyFinishedAsync()
    {
        var finished = (await Task.WhenAll(Players.Select(async p => (p.Key, Final: await p.Value.Probe.NextAsync("QuizFinished")))))
            .ToDictionary(x => x.Key, x => (Total: x.Final.GetProperty("totalPoints").GetInt32(), Rank: x.Final.GetProperty("rank").GetInt32()));
        var board = (await Organizer.Probe.NextAsync("QuizFinished")).GetProperty("leaderboard").EnumerateArray()
            .Select(e => (Rank: e.GetProperty("rank").GetInt32(), Name: e.GetProperty("username").GetString()!, Total: e.GetProperty("totalPoints").GetInt32()))
            .ToList();

        await using var db = _factory.CreateDbContext();
        var names = await db.Players.Where(p => p.QuizId == QuizId).ToDictionaryAsync(p => p.UserId, p => p.DisplayName);
        var rows = await db.Answers.Where(a => a.QuizId == QuizId).ToListAsync();
        Assert.Equal(Names.Count, names.Count);

        // The rows are the accepted answers, scored from the inputs alone (correct option, time left).
        var correct = Set.Questions.ToDictionary(q => q.Id, q => q.CorrectOptionId);
        var expectedRows = Accepted
            .Select(a => (a.Player, a.QuestionId, a.OptionId, Points: Scoring.Points(a.OptionId == correct[a.QuestionId], Limit - a.At, Limit)))
            .OrderBy(r => r.Player).ThenBy(r => r.QuestionId).ToList();
        var storedRows = rows
            .Select(r => (Player: names[r.UserId], r.QuestionId, r.OptionId, r.Points))
            .OrderBy(r => r.Player).ThenBy(r => r.QuestionId).ToList();
        Assert.Equal(expectedRows, storedRows);

        // Totals and competition ranks recomputed from the rows (not from Players.TotalPoints).
        var totals = Names.ToDictionary(n => n, n => rows.Where(r => names[r.UserId] == n).Sum(r => r.Points));
        int RankOf(string n) => 1 + totals.Values.Count(t => t > totals[n]);
        Assert.All(Names, n => Assert.Equal((totals[n], RankOf(n)), finished[n]));
        Assert.Equal(
            Names.Select(n => (RankOf(n), n, totals[n])).OrderBy(x => x).ToList(),
            board.Select(b => (b.Rank, b.Name, b.Total)).OrderBy(x => x).ToList());
    }

    /// <summary>Every event any client of this session received belongs to this session.</summary>
    public void VerifyNoForeignEvents()
    {
        var questionIds = Set.Questions.Select(q => q.Id).ToHashSet();
        var names = Names.ToHashSet();
        foreach (var client in AllClients)
        {
            foreach (var (name, payload) in client.Received)
            {
                if (payload.TryGetProperty("questionId", out var questionId))
                {
                    Assert.Contains(questionId.GetString()!, questionIds);
                }
                if (payload.TryGetProperty("username", out var username))
                {
                    Assert.Contains(username.GetString()!, names);
                }
                foreach (var listName in new[] { "top10", "leaderboard" })
                {
                    if (payload.TryGetProperty(listName, out var list))
                    {
                        Assert.All(list.EnumerateArray(), e => Assert.Contains(e.GetProperty("username").GetString()!, names));
                    }
                }
            }
        }
        foreach (var client in Players.Values.Append(Organizer))
        {
            Assert.Single(client.Received, e => e.Name == "QuizFinished");
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in Players.Values.Append(Organizer))
        {
            await client.DisposeAsync();
        }
    }

    /// <summary>
    /// Plays one question on every session at once on the shared fake clock: all clients see it
    /// start, answers are sent at their planned times (all players due at the same time at once),
    /// the deadline passes, late answers are refused, every client sees the close, the reveal ends.
    /// </summary>
    public static async Task PlayQuestionAsync(QuizApiFactory factory, IReadOnlyList<E2ESession> sessions, int index, Random random,
        Func<E2ESession, string, Task>? afterStart = null, ISet<string>? mustAnswer = null)
    {
        var questionIds = await Task.WhenAll(sessions.Select(s => s.QuestionStartedAsync(index)));
        if (afterStart is not null)
        {
            for (var s = 0; s < sessions.Count; s++)
            {
                await afterStart(sessions[s], questionIds[s]);
            }
        }

        var plans = sessions.Select(s => s.Plan(random, index, mustAnswer ?? new HashSet<string>())).ToList();
        var elapsed = TimeSpan.Zero;
        foreach (var at in plans.SelectMany(p => p).Where(a => a.At is not null).Select(a => a.At!.Value).Distinct().Order())
        {
            await factory.AdvanceAsync(at - elapsed);
            elapsed = at;
            await Task.WhenAll(sessions.Select((s, n) => s.AnswerAsync(questionIds[n], plans[n].Where(a => a.At == at), at)));
        }

        await factory.AdvanceAsync(Limit - elapsed); // the deadline closes every question
        await Task.WhenAll(sessions.Select((s, n) => s.AnswerLateAsync(questionIds[n], plans[n].Where(a => a.Late))));
        await Task.WhenAll(sessions.Select((s, n) => s.QuestionClosedAsync(questionIds[n])));
        await factory.AdvanceAsync(Reveal); // the next question, or the finish
    }

    /// <summary>
    /// No unexpected error was logged during the run. SignalR logs every <see cref="HubException"/>
    /// at Error level, so the planned late answers (code <c>QuestionClosed</c>) are expected, one each.
    /// </summary>
    public static void AssertNoUnexpectedErrorsLogged(QuizApiFactory factory, int lateAnswers)
    {
        var errors = factory.Logs.Entries.Where(e => e.Level >= LogLevel.Error).ToList();
        static bool IsLateAnswer(CapturingLoggerProvider.LogEntry e) => e.Exception is HubException { Message: "QuestionClosed" };
        var unexpected = errors.Where(e => !IsLateAnswer(e))
            .Select(e => $"{e.Category}: {e.Message} {e.Exception?.GetType().Name}: {e.Exception?.Message}").Distinct().ToList();
        Assert.True(unexpected.Count == 0, string.Join(Environment.NewLine, unexpected));
        Assert.Equal(lateAnswers, errors.Count(IsLateAnswer));
    }
}
