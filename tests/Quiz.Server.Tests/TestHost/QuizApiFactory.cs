using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Quiz.Server.Runtime;

namespace Quiz.Server.Tests.TestHost;

/// <summary>
/// Hosts the real server in memory on its own SQLite file (migrated at startup), so every
/// factory owns its data. Time is a <see cref="FakeTimeProvider"/> the test moves, pushes are
/// recorded, and logs are captured. The SPA build output (wwwroot) is gitignored and may not
/// exist (CI runs the server tests before the web build), so each factory also gets its own web
/// root with a minimal index.html. In Development a built wwwroot can still win through static
/// web assets, so tests look for the SPA mount point that both pages share.
/// </summary>
public class QuizApiFactory : WebApplicationFactory<Program>
{
    public const string SpaMarker = "<div id=\"root\"></div>";

    public static readonly DateTimeOffset StartTime = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    public string Environment { get; init; } = "Development";

    /// <summary>The folder holding the web root and the database. Share it to "restart" the server on the same file.</summary>
    public string Root { get; init; } = Path.Combine(Path.GetTempPath(), "quiz-tests", Guid.NewGuid().ToString("N"));

    /// <summary>False for a server that another factory restarts on the same files.</summary>
    public bool DeleteFilesOnDispose { get; init; } = true;

    public FakeTimeProvider Time { get; init; } = new(StartTime);

    public RecordingSessionEvents Events { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public string DataDirectory => Path.Combine(Root, "data");

    public string DatabasePath => Path.Combine(DataDirectory, "quiz.db");

    public SessionRegistry Registry => Services.GetRequiredService<SessionRegistry>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var webRoot = Path.Combine(Root, "wwwroot");
        Directory.CreateDirectory(webRoot);
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(Path.Combine(webRoot, "index.html"), $"<!doctype html><title>Quiz test SPA</title>{SpaMarker}");

        builder.UseEnvironment(Environment);
        builder.UseWebRoot(webRoot);
        builder.UseSetting("ConnectionStrings:Quiz", $"Data Source={DatabasePath}");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.RemoveAll<ISessionEvents>();
            services.AddSingleton<ISessionEvents>(Events);
        });
    }

    /// <summary>Moves the clock, then waits until every live session actor has handled what that triggered.</summary>
    public async Task AdvanceAsync(TimeSpan by)
    {
        Time.Advance(by);
        await Registry.FlushAllAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools(); // pooled connections keep the file open on Windows
            if (DeleteFilesOnDispose && Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
