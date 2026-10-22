using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace Quiz.Server.Features.Auth;

/// <summary>Login, logout and the current user. Cookie auth on the same origin covers REST and the hub.</summary>
public static class AuthFeature
{
    public const string CookieName = "quiz.auth";

    public static IServiceCollection AddAuthFeature(this IServiceCollection services)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.SlidingExpiration = true;
                // An API answers 401/403; the SPA shows its own login page.
                options.Events.OnRedirectToLogin = context => SetStatus(context.Response, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => SetStatus(context.Response, StatusCodes.Status403Forbidden);
            });

        // Deny by default: every endpoint needs a logged-in user unless it opts out with AllowAnonymous.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    public static IEndpointRouteBuilder MapAuthFeature(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/login", AuthEndpoints.Login)
            .AllowAnonymous()
            .WithName("Login")
            .WithSummary("Logs in with a seeded username and password and sets the auth cookie.")
            .Accepts<LoginRequest>("application/json")
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Cast: a (HttpContext) -> Task<IResult> method would otherwise bind as a RequestDelegate and drop the result.
        auth.MapPost("/logout", (Delegate)AuthEndpoints.Logout)
            .AllowAnonymous()
            .WithName("Logout")
            .WithSummary("Clears the auth cookie.")
            .Produces(StatusCodes.Status204NoContent);

        auth.MapGet("/me", AuthEndpoints.Me)
            .WithName("GetCurrentUser")
            .WithSummary("Returns the logged-in user.")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static Task SetStatus(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
