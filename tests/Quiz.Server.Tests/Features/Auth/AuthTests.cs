using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Quiz.Server.Tests.TestHost;

namespace Quiz.Server.Tests.Features.Auth;

public class AuthTests
{
    private sealed record UserResponse(Guid UserId, string Username);

    [Theory]
    [InlineData("alice")]
    [InlineData("ALICE")]
    public async Task Login_ValidCredentials_Returns200WithTheUserAndSetsTheAuthCookie(string typed)
    {
        using var factory = new QuizApiFactory();
        var userId = await factory.AddUserAsync("alice");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = typed, password = TestData.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new UserResponse(userId, "alice"), await response.Content.ReadFromJsonAsync<UserResponse>());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("quiz.auth=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Me_AfterLogin_ReturnsThatUser()
    {
        using var factory = new QuizApiFactory();
        var userId = await factory.AddUserAsync("alice");
        var client = await factory.LoginAsync("alice");

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new UserResponse(userId, "alice"), await response.Content.ReadFromJsonAsync<UserResponse>());
    }

    [Theory]
    [InlineData("alice", "wrong-password")]
    [InlineData("nobody", TestData.Password)]
    public async Task Login_WrongPasswordOrUnknownUser_Returns401Problem(string username, string password)
    {
        using var factory = new QuizApiFactory();
        await factory.AddUserAsync("alice");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Wrong username or password", problem.GetProperty("title").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("", "secret", "username")]
    [InlineData("   ", "secret", "username")]
    [InlineData("alice", "", "password")]
    public async Task Login_EmptyField_Returns400ValidationForThatField(string username, string password, string field)
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"no error for {field}: {problem}");
    }

    [Fact]
    public async Task Login_MissingBody_Returns400()
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/auth/login", JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Me_Anonymous_Returns401Problem()
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Logout_Returns204AndEndsTheSession()
    {
        using var factory = new QuizApiFactory();
        await factory.AddUserAsync("alice");
        var client = await factory.LoginAsync("alice");

        var logout = await client.PostAsync("/api/auth/logout", null);
        var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task OpenApi_DescribesTheAuthEndpoints()
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient();

        var document = await client.GetStringAsync("/openapi/v1.json");

        Assert.Contains("\"/api/auth/login\"", document);
        Assert.Contains("\"/api/auth/logout\"", document);
        Assert.Contains("\"/api/auth/me\"", document);
    }
}
