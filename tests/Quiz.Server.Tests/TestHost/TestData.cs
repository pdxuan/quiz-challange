using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quiz.Server.Data;
using Quiz.Server.Domain;

namespace Quiz.Server.Tests.TestHost;

/// <summary>Arranges data straight in the test's own SQLite file, and logs clients in.</summary>
public static class TestData
{
    public const string Password = "pass-1234";

    public static QuizDbContext CreateDbContext(this QuizApiFactory factory) =>
        factory.Services.GetRequiredService<IDbContextFactory<QuizDbContext>>().CreateDbContext();

    public static async Task<Guid> AddUserAsync(this QuizApiFactory factory, string username, string password = Password)
    {
        await using var db = factory.CreateDbContext();
        var user = new User(Guid.NewGuid(), username);
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.UserId;
    }

    /// <summary>A quiz set whose every question has options a-d with "a" correct.</summary>
    public static QuizSet BuildQuizSet(string id = "set-1", int questions = 3, int timeLimitSec = 15)
    {
        var set = new QuizSet(id, $"Quiz {id}", timeLimitSec);
        for (var i = 0; i < questions; i++)
        {
            var questionId = $"{id}-q{i + 1}";
            set.Questions.Add(new Question(questionId, id, i, $"Word {i + 1}", QuestionDirection.WordToMeaning, "a",
                new[] { "a", "b", "c", "d" }.Select((o, n) => new Option(o, questionId, n, $"meaning {o}")).ToList()));
        }
        return set;
    }

    public static async Task<QuizSet> AddQuizSetAsync(this QuizApiFactory factory, string id = "set-1", int questions = 3, int timeLimitSec = 15)
    {
        await using var db = factory.CreateDbContext();
        var set = BuildQuizSet(id, questions, timeLimitSec);
        db.QuizSets.Add(set);
        await db.SaveChangesAsync();
        return set;
    }

    /// <summary>A client that carries the auth cookie of <paramref name="username"/>.</summary>
    public static async Task<HttpClient> LoginAsync(this QuizApiFactory factory, string username, string password = Password)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        return client;
    }
}
