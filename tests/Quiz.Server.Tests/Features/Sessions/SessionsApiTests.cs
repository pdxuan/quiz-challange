using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quiz.Server.Domain;
using Quiz.Server.Tests.TestHost;

namespace Quiz.Server.Tests.Features.Sessions;

public class SessionsApiTests
{
    private sealed record CreatedResponse(string QuizId);

    private sealed record LeaderboardEntry(int Rank, string Username, int TotalPoints, bool Online);

    private sealed record LeaderboardResponse(string State, List<LeaderboardEntry> Entries);

    private static async Task<string> ProblemTitleAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;
    }

    [Fact]
    public async Task Create_KnownQuizSet_Returns201WithLocationAndStoresALobbySessionOwnedByTheCaller()
    {
        using var factory = new QuizApiFactory();
        var set = await factory.AddQuizSetAsync();
        var organizer = await factory.AddUserAsync("org");
        var client = await factory.LoginAsync("org");

        var response = await client.PostAsJsonAsync("/api/sessions", new { quizSetId = set.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var quizId = (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.QuizId;
        Assert.True(QuizIdGenerator.IsValid(quizId), quizId);
        Assert.Equal($"/api/sessions/{quizId}/leaderboard", response.Headers.Location?.ToString());
        await using var db = factory.CreateDbContext();
        var stored = await db.Sessions.SingleAsync(s => s.QuizId == quizId);
        Assert.Equal((SessionState.Lobby, organizer, set.Id), (stored.State, stored.OrganizerUserId, stored.QuizSetId));
    }

    [Fact]
    public async Task Create_UnknownQuizSet_Returns404()
    {
        using var factory = new QuizApiFactory();
        await factory.AddUserAsync("org");
        var client = await factory.LoginAsync("org");

        var response = await client.PostAsJsonAsync("/api/sessions", new { quizSetId = "nope" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Quiz set not found", await ProblemTitleAsync(response));
    }

    [Fact]
    public async Task Create_MissingQuizSetId_Returns400Validation()
    {
        using var factory = new QuizApiFactory();
        await factory.AddUserAsync("org");
        var client = await factory.LoginAsync("org");

        var response = await client.PostAsJsonAsync("/api/sessions", new { quizSetId = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("quizSetId", out _), problem.ToString());
    }

    [Theory]
    [InlineData("POST", "/api/sessions")]
    [InlineData("POST", "/api/sessions/ABC234/start")]
    [InlineData("GET", "/api/sessions/ABC234/leaderboard")]
    public async Task SessionEndpoints_Anonymous_Return401(string method, string path)
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { quizSetId = "set-1" }) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Start_ByTheOrganizerWithAPlayer_Returns200StoresRunningAndPublishesTheFirstQuestion()
    {
        using var factory = new QuizApiFactory();
        var (quizId, _) = await factory.AddLobbyAsync();
        await factory.JoinAsync(quizId, await factory.AddUserAsync("alice"), "alice");
        var client = await factory.LoginAsync("org");

        var response = await client.PostAsync($"/api/sessions/{quizId.ToLowerInvariant()}/start", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Running", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
        await using var db = factory.CreateDbContext();
        var stored = await db.Sessions.SingleAsync(s => s.QuizId == quizId);
        Assert.Equal((SessionState.Running, 0, SessionPhase.Question), (stored.State, stored.CurrentIndex, stored.Phase));
        var started = Assert.Single(factory.Events.For(quizId).OfType<QuestionStarted>());
        Assert.Equal(0, started.Index);
        Assert.Equal(QuizApiFactory.StartTime.AddSeconds(15), started.EndsAt); // the event type has no correct option at all
    }

    [Fact]
    public async Task Start_ByAnotherUser_Returns403()
    {
        using var factory = new QuizApiFactory();
        var (quizId, _) = await factory.AddLobbyAsync();
        await factory.JoinAsync(quizId, await factory.AddUserAsync("alice"), "alice");
        var client = await factory.LoginAsync("alice");

        var response = await client.PostAsync($"/api/sessions/{quizId}/start", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("NotOrganizer", await ProblemTitleAsync(response));
    }

    [Fact]
    public async Task Start_WithNoPlayers_Returns409()
    {
        using var factory = new QuizApiFactory();
        var (quizId, _) = await factory.AddLobbyAsync();
        var client = await factory.LoginAsync("org");

        var response = await client.PostAsync($"/api/sessions/{quizId}/start", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("NoPlayers", await ProblemTitleAsync(response));
    }

    [Fact]
    public async Task Start_WithTheDefaultRules_NeedsTwoOnlinePlayers()
    {
        using var factory = new QuizApiFactory { MinPlayersToStart = null }; // the shipped config, not the test default
        var (quizId, organizer) = await factory.AddLobbyAsync();
        await factory.JoinAsync(quizId, organizer, "org"); // the organizer plays too
        var client = await factory.LoginAsync("org");

        var alone = await client.PostAsync($"/api/sessions/{quizId}/start", null);
        await factory.JoinAsync(quizId, await factory.AddUserAsync("alice"), "alice");
        var withTwo = await client.PostAsync($"/api/sessions/{quizId}/start", null);

        Assert.Equal((HttpStatusCode.Conflict, "NoPlayers"), (alone.StatusCode, await ProblemTitleAsync(alone)));
        Assert.Equal(HttpStatusCode.OK, withTwo.StatusCode);
    }

    [Fact]
    public async Task Start_Twice_Returns409()
    {
        using var factory = new QuizApiFactory();
        var (quizId, _) = await factory.AddLobbyAsync();
        await factory.JoinAsync(quizId, await factory.AddUserAsync("alice"), "alice");
        var client = await factory.LoginAsync("org");
        await client.PostAsync($"/api/sessions/{quizId}/start", null);

        var response = await client.PostAsync($"/api/sessions/{quizId}/start", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("NotInLobby", await ProblemTitleAsync(response));
    }

    [Theory]
    [InlineData("ZZZ999")] // well-formed, no such session
    [InlineData("not-an-id")]
    public async Task Start_UnknownQuiz_Returns404(string quizId)
    {
        using var factory = new QuizApiFactory();
        await factory.AddUserAsync("org");
        var client = await factory.LoginAsync("org");

        var response = await client.PostAsync($"/api/sessions/{quizId}/start", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("QuizNotFound", await ProblemTitleAsync(response));
    }

    [Fact]
    public async Task Leaderboard_ReturnsTheFullOrderedListFromTheDatabase()
    {
        using var factory = new QuizApiFactory();
        var (quizId, organizer) = await factory.AddLobbyAsync(questions: 2);
        var alice = await factory.AddUserAsync("alice");
        var bob = await factory.AddUserAsync("bob");
        await factory.JoinAsync(quizId, alice, "alice");
        await factory.JoinAsync(quizId, bob, "bob");
        await factory.Registry.AskAsync(quizId, (s, call) => { s.Start(organizer, call.Now, call.Rules); return 0; });
        await AnswerAsync(factory, quizId, bob, "a", after: TimeSpan.FromSeconds(3));   // 900
        await AnswerAsync(factory, quizId, alice, "b", after: TimeSpan.FromSeconds(4)); // wrong; all answered -> closed
        var client = await factory.LoginAsync("alice");

        var board = await client.GetFromJsonAsync<LeaderboardResponse>($"/api/sessions/{quizId}/leaderboard");

        Assert.Equal("Running", board!.State);
        Assert.Equal([new LeaderboardEntry(1, "bob", 900, true), new LeaderboardEntry(2, "alice", 0, true)], board.Entries);
    }

    [Fact]
    public async Task Leaderboard_WhileAQuestionIsOpen_HidesItsPoints()
    {
        using var factory = new QuizApiFactory();
        var (quizId, organizer) = await factory.AddLobbyAsync();
        var alice = await factory.AddUserAsync("alice");
        await factory.JoinAsync(quizId, alice, "alice");
        await factory.JoinAsync(quizId, await factory.AddUserAsync("bob"), "bob");
        await factory.Registry.AskAsync(quizId, (s, call) => { s.Start(organizer, call.Now, call.Rules); return 0; });
        await AnswerAsync(factory, quizId, alice, "a", after: TimeSpan.FromSeconds(1)); // bob has not answered: still open
        var client = await factory.LoginAsync("alice");

        var board = await client.GetFromJsonAsync<LeaderboardResponse>($"/api/sessions/{quizId}/leaderboard");

        Assert.All(board!.Entries, e => Assert.Equal(0, e.TotalPoints));
    }

    [Fact]
    public async Task Leaderboard_UnknownQuiz_Returns404()
    {
        using var factory = new QuizApiFactory();
        await factory.AddUserAsync("alice");
        var client = await factory.LoginAsync("alice");

        var response = await client.GetAsync("/api/sessions/ZZZ999/leaderboard");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("QuizNotFound", await ProblemTitleAsync(response));
    }

    [Fact]
    public async Task OpenApi_DescribesTheSessionEndpoints()
    {
        using var factory = new QuizApiFactory();
        var document = await factory.CreateClient().GetStringAsync("/openapi/v1.json");

        Assert.Contains("\"/api/sessions\"", document);
        Assert.Contains("\"/api/sessions/{quizId}/start\"", document);
        Assert.Contains("\"/api/sessions/{quizId}/leaderboard\"", document);
    }

    private static async Task AnswerAsync(QuizApiFactory factory, string quizId, Guid userId, string optionId, TimeSpan after)
    {
        factory.Time.Advance(after - (factory.Time.GetUtcNow() - QuizApiFactory.StartTime));
        var receivedAt = factory.Time.GetUtcNow();
        await factory.Registry.AskAsync(quizId, (s, call) =>
            s.SubmitAnswer(userId, s.CurrentQuestion!.Id, optionId, Guid.NewGuid(), receivedAt, call.Now, call.Rules));
    }
}
