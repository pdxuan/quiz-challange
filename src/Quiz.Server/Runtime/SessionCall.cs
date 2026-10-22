using Quiz.Server.Domain;

namespace Quiz.Server.Runtime;

/// <summary>What a call to a session gets besides the session: the server time and the rules.</summary>
public sealed record SessionCall(DateTimeOffset Now, SessionRules Rules);

/// <summary>An unexpected failure inside a session call. Carries no detail; the cause is logged.</summary>
public sealed class SessionActorException() : Exception("The session could not handle the request.");
