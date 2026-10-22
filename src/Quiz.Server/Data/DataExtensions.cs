using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Quiz.Server.Data;

public static class DataExtensions
{
    private const string DefaultConnection = "Data Source=App_Data/quiz.db";

    /// <summary>How long a command waits for SQLite's write lock before it fails.</summary>
    private const int BusyTimeoutSeconds = 5;

    /// <summary>
    /// One DbContext per actor message or request, from a factory. The connection string comes
    /// from <c>ConnectionStrings:Quiz</c>; a relative file path is under the content root.
    /// </summary>
    public static IServiceCollection AddQuizData(this IServiceCollection services)
    {
        services.AddDbContextFactory<QuizDbContext>((sp, options) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var environment = sp.GetRequiredService<IHostEnvironment>();
            options.UseSqlite(ResolveConnectionString(configuration.GetConnectionString("Quiz") ?? DefaultConnection, environment.ContentRootPath));
        });
        return services;
    }

    /// <summary>Applies pending migrations and switches the file to WAL (readers do not block the writer).</summary>
    public static async Task MigrateQuizDatabaseAsync(this WebApplication app)
    {
        var factory = app.Services.GetRequiredService<IDbContextFactory<QuizDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        var folder = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder); // SQLite creates the file, not its folder
        }

        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }

    private static string ResolveConnectionString(string connectionString, string contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString) { DefaultTimeout = BusyTimeoutSeconds };
        if (!Path.IsPathRooted(builder.DataSource) && builder.DataSource != ":memory:")
        {
            builder.DataSource = Path.Combine(contentRoot, builder.DataSource);
        }
        return builder.ToString();
    }
}
