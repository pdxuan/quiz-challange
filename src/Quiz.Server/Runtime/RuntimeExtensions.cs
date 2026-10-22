using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Quiz.Server.Runtime;

public static class RuntimeExtensions
{
    /// <summary>Session actors, their registry, recovery after a restart, the server clock and the events port.</summary>
    public static IServiceCollection AddQuizRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SessionOptions>(configuration.GetSection(SessionOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISessionEvents, NullSessionEvents>(); // S4 registers the SignalR implementation
        services.AddSingleton<SessionRegistry>();
        services.AddHostedService<SessionRecoveryService>();
        return services;
    }
}
