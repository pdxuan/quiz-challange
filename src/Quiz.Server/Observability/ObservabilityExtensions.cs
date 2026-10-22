namespace Quiz.Server.Observability;

public static class ObservabilityExtensions
{
    /// <summary>JSON console logs with scopes (quizId, userId), the quiz meter and health checks.</summary>
    public static WebApplicationBuilder AddQuizObservability(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

        builder.Services.AddMetrics();
        builder.Services.AddSingleton<QuizMetrics>();
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        return builder;
    }

    public static IEndpointRouteBuilder MapQuizHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health").AllowAnonymous();
        return app;
    }
}
