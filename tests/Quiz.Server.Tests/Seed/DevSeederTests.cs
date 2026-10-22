using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quiz.Server.Seed;
using Quiz.Server.Tests.TestHost;

namespace Quiz.Server.Tests.Seed;

public class DevSeederTests
{
    /// <summary>A test server with dev seeding switched on (the shared factory keeps it off: tests own their data).</summary>
    private sealed class SeedingFactory : QuizApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(config =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { [SeedFeature.EnabledKey] = "true" }));
        }
    }

    [Fact]
    public async Task Startup_InDevelopmentWithAnEmptyDatabase_SeedsThreeUsersAndTwoValidQuizSets()
    {
        using var factory = new SeedingFactory();
        _ = factory.Server; // starts the app: migrate, then seed

        await using var db = factory.CreateDbContext();
        var users = await db.Users.OrderBy(u => u.Username).Select(u => u.Username).ToListAsync();
        Assert.Equal(["alice", "bob", "carol"], users);
        var sets = await db.QuizSets.Include(s => s.Questions).ThenInclude(q => q.Options).ToListAsync();
        Assert.Equal(2, sets.Count);
        Assert.All(sets, set =>
        {
            Assert.Empty(SeedValidator.Validate(set));
            Assert.InRange(set.Questions.Count, 8, 12);
        });
    }

    [Fact]
    public async Task Startup_InDevelopment_SeededUsersLogInWithTheirDevPasswords()
    {
        using var factory = new SeedingFactory();
        var root = factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().ContentRootPath;

        foreach (var user in DevSeeder.LoadUsers(root))
        {
            var client = await factory.LoginAsync(user.Username, user.Password);
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/quiz-sets")).StatusCode);
        }
    }

    [Fact]
    public async Task Startup_InDevelopmentWithANonEmptyDatabase_TouchesNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiz-tests", Guid.NewGuid().ToString("N"));
        using (var first = new QuizApiFactory { Root = root, DeleteFilesOnDispose = false })
        {
            await first.AddUserAsync("zed");
        }

        using var factory = new SeedingFactory { Root = root };
        _ = factory.Server;

        await using var db = factory.CreateDbContext();
        Assert.Equal(["zed"], await db.Users.Select(u => u.Username).ToListAsync());
        Assert.Empty(await db.QuizSets.ToListAsync());
    }

    [Fact]
    public async Task Startup_InProduction_SeedsNothing()
    {
        using var factory = new SeedingFactory { Environment = "Production" };
        _ = factory.Server;

        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.QuizSets.ToListAsync());
    }

    [Fact]
    public void SeedFiles_EveryQuizSetPassesTheValidatorAndUsesOneDirectionPerWord()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Quiz.Server"));

        var sets = DevSeeder.LoadQuizSets(root);

        Assert.Equal(2, sets.Count);
        Assert.All(sets, set => Assert.Empty(SeedValidator.Validate(set)));
        Assert.All(sets, set => Assert.Contains(set.Questions, q => q.Direction == Quiz.Server.Domain.QuestionDirection.MeaningToWord));
        Assert.Equal(3, DevSeeder.LoadUsers(root).Count);
    }
}
