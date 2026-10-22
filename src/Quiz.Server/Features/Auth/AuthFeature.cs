namespace Quiz.Server.Features.Auth;

/// <summary>Login, logout and the current user (filled in S2).</summary>
public static class AuthFeature
{
    public static IServiceCollection AddAuthFeature(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapAuthFeature(this IEndpointRouteBuilder app) => app;
}
