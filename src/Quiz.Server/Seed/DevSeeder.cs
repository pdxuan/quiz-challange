// AI-assisted: Claude Code (Claude Opus 5.5), slice S6; reviewed by a reviewer agent and a human; verified by Seed/DevSeederTests, mutations (docs/ai-collab/s6.md).
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Quiz.Server.Data;
using Quiz.Server.Domain;

namespace Quiz.Server.Seed;

/// <summary>A dev login from <c>seed/users.json</c>. Mock dev data only: never reuse these passwords anywhere real.</summary>
public sealed record SeedUser(string Username, string Password);

/// <summary>Loads the dev users and quiz sets from <c>Seed/seed/*.json</c> and stores them in an empty database.</summary>
public static class DevSeeder
{
    private sealed record OptionJson(string Id, string Text);

    private sealed record QuestionJson(string Id, string Prompt, QuestionDirection Direction, string CorrectOptionId, List<OptionJson> Options);

    private sealed record QuizSetJson(string Id, string Title, int TimeLimitSec, List<QuestionJson> Questions);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string SeedFolder(string contentRoot) => Path.Combine(contentRoot, "Seed", "seed");

    public static IReadOnlyList<SeedUser> LoadUsers(string contentRoot) =>
        Read<List<SeedUser>>(Path.Combine(SeedFolder(contentRoot), "users.json"));

    /// <summary>The quiz sets in file order; a question's position is its place in the file.</summary>
    public static IReadOnlyList<QuizSet> LoadQuizSets(string contentRoot) =>
        Read<List<QuizSetJson>>(Path.Combine(SeedFolder(contentRoot), "quiz-sets.json")).Select(s =>
        {
            var set = new QuizSet(s.Id, s.Title, s.TimeLimitSec);
            set.Questions.AddRange(s.Questions.Select((q, position) => new Question(q.Id, s.Id, position, q.Prompt, q.Direction, q.CorrectOptionId,
                q.Options.Select((o, n) => new Option(o.Id, q.Id, n, o.Text)).ToList())));
            return set;
        }).ToList();

    /// <summary>
    /// Seeds the users and quiz sets if the database holds no users, quiz sets or sessions; a
    /// database with any of them is not touched. Throws if a quiz set fails the validator.
    /// </summary>
    /// <returns>True if it seeded.</returns>
    public static async Task<bool> SeedAsync(QuizDbContext db, string contentRoot, CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken) || await db.QuizSets.AnyAsync(cancellationToken) || await db.Sessions.AnyAsync(cancellationToken))
        {
            return false;
        }

        var sets = LoadQuizSets(contentRoot);
        var errors = sets.SelectMany(SeedValidator.Validate).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("The seed quiz sets are invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        var hasher = new PasswordHasher<User>();
        foreach (var seed in LoadUsers(contentRoot))
        {
            var user = new User(Guid.NewGuid(), seed.Username);
            user.PasswordHash = hasher.HashPassword(user, seed.Password);
            db.Users.Add(user);
        }
        db.QuizSets.AddRange(sets);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidOperationException($"Seed file {path} is empty.");
}
