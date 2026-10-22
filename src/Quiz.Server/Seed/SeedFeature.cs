using Microsoft.EntityFrameworkCore;
using Quiz.Server.Data;

namespace Quiz.Server.Seed;

/// <summary>Seeds dev users and quiz sets into an empty database, only in Development.</summary>
public static class SeedFeature
{
    /// <summary>On by default in Development; the test host turns it off, because tests create their own data.</summary>
    public const string EnabledKey = "Seed:Enabled";

    public static async Task SeedDevDataAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue(EnabledKey, defaultValue: true))
        {
            return;
        }

        var factory = app.Services.GetRequiredService<IDbContextFactory<QuizDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var seeded = await DevSeeder.SeedAsync(db, app.Environment.ContentRootPath);
        app.Logger.LogInformation(seeded ? "Seeded the dev users and quiz sets" : "Database not empty: dev seed skipped");
    }
}
