// AI-assisted: Claude Code (Claude Opus 5.5), slice S2; reviewed by a reviewer agent and a human; verified by Features/Auth/AuthTests (docs/ai-collab/s2.md).
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Quiz.Server.Data;

namespace Quiz.Server.Features.Auth;

public sealed record LoginRequest(string? Username, string? Password)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Username))
        {
            errors["username"] = ["Username is required."];
        }
        if (string.IsNullOrEmpty(Password))
        {
            errors["password"] = ["Password is required."];
        }
        return errors;
    }
}

public sealed record UserResponse(Guid UserId, string Username);

internal static class AuthEndpoints
{
    private static readonly PasswordHasher<User> Hasher = new();

    public static async Task<IResult> Login(LoginRequest request, IDbContextFactory<QuizDbContext> dbFactory, HttpContext http)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        await using var db = await dbFactory.CreateDbContextAsync(http.RequestAborted);
        var username = request.Username!.Trim();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == username, http.RequestAborted); // NOCASE column
        if (user is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password!) == PasswordVerificationResult.Failed)
        {
            // One message for both cases, so the API does not reveal which usernames exist.
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Wrong username or password");
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()), new Claim(ClaimTypes.Name, user.Username)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Ok(new UserResponse(user.UserId, user.Username));
    }

    public static async Task<IResult> Logout(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    public static IResult Me(ClaimsPrincipal user) =>
        Results.Ok(new UserResponse(Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), user.Identity!.Name!));
}
