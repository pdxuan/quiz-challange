using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Quiz.Server.Tests.TestHost;

namespace Quiz.Server.Tests.Features.QuizSets;

public class QuizSetsApiTests
{
    private sealed record QuizSetSummary(string Id, string Title, int QuestionCount, int TimeLimitSec);

    [Fact]
    public async Task List_LoggedIn_ReturnsEveryStoredSetWithoutAnyAnswer()
    {
        using var factory = new QuizApiFactory();
        await factory.AddQuizSetAsync("set-b", questions: 4, timeLimitSec: 20);
        await factory.AddQuizSetAsync("set-a", questions: 2, timeLimitSec: 10);
        await factory.AddUserAsync("alice");
        var client = await factory.LoginAsync("alice");

        var response = await client.GetAsync("/api/quiz-sets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var sets = JsonSerializer.Deserialize<List<QuizSetSummary>>(body, JsonSerializerOptions.Web)!;
        Assert.Equal([new("set-a", "Quiz set-a", 2, 10), new("set-b", "Quiz set-b", 4, 20)], sets);
        // Only the summary fields: no questions, options or correct option ids.
        var fields = JsonDocument.Parse(body).RootElement.EnumerateArray().SelectMany(s => s.EnumerateObject().Select(p => p.Name)).Distinct();
        Assert.Equal(["id", "title", "questionCount", "timeLimitSec"], fields);
        Assert.DoesNotContain("correctOptionId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meaning a", body);
    }

    [Fact]
    public async Task List_NoSets_ReturnsAnEmptyList()
    {
        using var factory = new QuizApiFactory();
        await factory.AddUserAsync("alice");
        var client = await factory.LoginAsync("alice");

        var sets = await client.GetFromJsonAsync<List<QuizSetSummary>>("/api/quiz-sets");

        Assert.Empty(sets!);
    }

    [Fact]
    public async Task List_Anonymous_Returns401()
    {
        using var factory = new QuizApiFactory();
        await factory.AddQuizSetAsync();

        var response = await factory.CreateClient().GetAsync("/api/quiz-sets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("set-1", await response.Content.ReadAsStringAsync());
    }
}
