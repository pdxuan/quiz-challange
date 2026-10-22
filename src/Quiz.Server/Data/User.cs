namespace Quiz.Server.Data;

/// <summary>A seeded account (organizers and players log in with these). No domain rules, so it lives with the data.</summary>
public class User(Guid userId, string username)
{
    public Guid UserId { get; private set; } = userId;
    public string Username { get; private set; } = username;
    public string PasswordHash { get; set; } = "";
}
