using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Quiz.Server.Tests.TestHost;

/// <summary>
/// Hosts the real server in memory on its own SQLite file (migrated at startup), so every
/// factory owns its data. The SPA build output (wwwroot) is gitignored and may not exist (CI
/// runs the server tests before the web build), so each factory also gets its own web root
/// with a minimal index.html. In Development a built wwwroot can still win through static web
/// assets, so tests look for the SPA mount point that both pages share.
/// </summary>
public class QuizApiFactory : WebApplicationFactory<Program>
{
    public const string SpaMarker = "<div id=\"root\"></div>";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiz-tests", Guid.NewGuid().ToString("N"));

    public string Environment { get; init; } = "Development";

    public string DataDirectory => Path.Combine(_root, "data");

    public string DatabasePath => Path.Combine(DataDirectory, "quiz.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var webRoot = Path.Combine(_root, "wwwroot");
        Directory.CreateDirectory(webRoot);
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(Path.Combine(webRoot, "index.html"), $"<!doctype html><title>Quiz test SPA</title>{SpaMarker}");

        builder.UseEnvironment(Environment);
        builder.UseWebRoot(webRoot);
        builder.UseSetting("ConnectionStrings:Quiz", $"Data Source={DatabasePath}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools(); // pooled connections keep the file open on Windows
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
