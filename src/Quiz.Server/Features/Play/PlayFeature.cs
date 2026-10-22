using Microsoft.Extensions.DependencyInjection.Extensions;
using Quiz.Server.Runtime;

namespace Quiz.Server.Features.Play;

/// <summary>The real-time quiz hub at /hubs/quiz and the SignalR side of the session events.</summary>
public static class PlayFeature
{
    public const string HubPath = "/hubs/quiz";

    public static IServiceCollection AddPlayFeature(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<SignalRSessionEvents>();
        services.Replace(ServiceDescriptor.Singleton<ISessionEvents>(sp => sp.GetRequiredService<SignalRSessionEvents>()));
        return services;
    }

    public static IEndpointRouteBuilder MapPlayFeature(this IEndpointRouteBuilder app)
    {
        app.MapHub<QuizHub>(HubPath);
        return app;
    }
}
