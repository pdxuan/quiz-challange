using Quiz.Server.Features.Auth;
using Quiz.Server.Features.Play;
using Quiz.Server.Features.QuizSets;
using Quiz.Server.Features.Sessions;
using Quiz.Server.Observability;
using Quiz.Server.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.AddQuizObservability();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// One registration per feature; LEAF slices fill their own Feature file, never this one.
builder.Services
    .AddAuthFeature()
    .AddQuizSetsFeature()
    .AddSessionsFeature()
    .AddPlayFeature();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Quiz API v1"));
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapQuizHealth();
app.MapAuthFeature();
app.MapQuizSetsFeature();
app.MapSessionsFeature();
app.MapPlayFeature();

// Server paths never fall through to the SPA: unknown ones are a 404 ProblemDetails.
foreach (var prefix in new[] { "/api", "/hubs", "/openapi", "/swagger" })
{
    app.MapFallback($"{prefix}/{{**path}}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound));
}
app.MapFallbackToFile("index.html");

await app.SeedDevDataAsync();

app.Run();

public partial class Program;
