using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Quiz.Server.Tests.TestHost;

/// <summary>
/// Hosts the real server in memory. The SPA build output (wwwroot) is gitignored and may not
/// exist (CI runs the server tests before the web build), so each factory gets its own web root
/// with a minimal index.html. In Development a built wwwroot can still win through static web
/// assets, so tests look for the SPA mount point that both pages share.
/// </summary>
public class QuizApiFactory : WebApplicationFactory<Program>
{
    public const string SpaMarker = "<div id=\"root\"></div>";

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "quiz-tests", Guid.NewGuid().ToString("N"));

    public string Environment { get; init; } = "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), $"<!doctype html><title>Quiz test SPA</title>{SpaMarker}");

        builder.UseEnvironment(Environment);
        builder.UseWebRoot(_webRoot);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }
}
