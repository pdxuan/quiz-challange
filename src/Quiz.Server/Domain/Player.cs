namespace Quiz.Server.Domain;

/// <summary>A user in one session. One per (quiz, user); a rejoin reuses it.</summary>
public class Player(string quizId, Guid userId, string displayName, DateTimeOffset joinedAt)
{
    public string QuizId { get; private set; } = quizId;
    public Guid UserId { get; private set; } = userId;
    public string DisplayName { get; private set; } = displayName;
    public string? ConnectionId { get; internal set; }
    public bool Online { get; internal set; }
    public DateTimeOffset JoinedAt { get; private set; } = joinedAt;
    public int TotalPoints { get; internal set; }

    /// <summary>When the total last went up (the join time while it is 0); breaks ties in the ranking.</summary>
    public DateTimeOffset ReachedAt { get; internal set; } = joinedAt;
}

/// <summary>A player's one answer to one question. Points are computed at receipt and revealed at close.</summary>
public class Answer(string quizId, Guid userId, string questionId, string optionId, Guid submissionId, DateTimeOffset receivedAt, int points)
{
    public string QuizId { get; private set; } = quizId;
    public Guid UserId { get; private set; } = userId;
    public string QuestionId { get; private set; } = questionId;
    public string OptionId { get; private set; } = optionId;
    public Guid SubmissionId { get; private set; } = submissionId;
    public DateTimeOffset ReceivedAt { get; private set; } = receivedAt;
    public int Points { get; private set; } = points;
}
