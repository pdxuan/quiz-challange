// AI-assisted: Claude Code (Claude Opus 5.5), slice S1; reviewed by a reviewer agent and a human; verified by Domain/Session*Tests, mutations (docs/ai-collab/s1.md).
namespace Quiz.Server.Domain;

/// <summary>
/// One live quiz: the state machine Lobby -> Running (Question/Reveal per question) -> Finished.
/// Pure: no threads, clock or storage. Every call gets <c>now</c>; rule failures throw a
/// <see cref="SessionException"/>; changes to push are collected in <see cref="Events"/>.
/// The actor applies one call at a time, so the methods never need a lock.
/// </summary>
public class Session
{
    private readonly List<SessionEvent> _events = [];

    private Session(string quizId, string quizSetId, Guid organizerUserId, DateTimeOffset createdAt)
    {
        QuizId = quizId;
        QuizSetId = quizSetId;
        OrganizerUserId = organizerUserId;
        CreatedAt = createdAt;
    }

    public string QuizId { get; private set; }
    public string QuizSetId { get; private set; }
    public QuizSet QuizSet { get; private set; } = null!;
    public Guid OrganizerUserId { get; private set; }
    public SessionState State { get; private set; } = SessionState.Lobby;
    public int CurrentIndex { get; private set; } = -1;
    public SessionPhase Phase { get; private set; } = SessionPhase.None;
    public DateTimeOffset? PhaseEndsAt { get; private set; }
    public DateTimeOffset? QuestionStartedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public List<Player> Players { get; private set; } = [];

    /// <summary>The answers loaded with the session (at least those for the current question).</summary>
    public List<Answer> Answers { get; private set; } = [];

    public IReadOnlyList<SessionEvent> Events => _events;

    public Question? CurrentQuestion =>
        State == SessionState.Running && CurrentIndex >= 0 ? QuizSet.Ordered[CurrentIndex] : null;

    public static Session Create(string quizId, QuizSet quizSet, Guid organizerUserId, DateTimeOffset now)
    {
        if (quizSet.Questions.Count == 0)
        {
            throw new SessionException(SessionError.Invalid);
        }
        return new Session(quizId, quizSet.Id, organizerUserId, now) { QuizSet = quizSet };
    }

    public void ClearEvents() => _events.Clear();

    /// <summary>
    /// The board anyone may see. While a question is open its points stay hidden (results show
    /// only when it closes), so each player is ranked as they stood before it. Needs every answer
    /// of the session loaded.
    /// </summary>
    public IReadOnlyList<RankedPlayer> Leaderboard()
    {
        if (Phase != SessionPhase.Question)
        {
            return Ranking.Rank(Players);
        }

        var open = CurrentQuestion!.Id;
        return Ranking.RankStandings(Players.Select(p =>
        {
            var hidden = Answers.FirstOrDefault(a => a.UserId == p.UserId && a.QuestionId == open);
            if (hidden is null || hidden.Points == 0)
            {
                return (p, p.TotalPoints, p.ReachedAt);
            }
            var reachedBefore = Answers
                .Where(a => a.UserId == p.UserId && a.QuestionId != open && a.Points > 0)
                .Select(a => a.ReceivedAt)
                .DefaultIfEmpty(p.JoinedAt)
                .Max();
            return (p, p.TotalPoints - hidden.Points, reachedBefore);
        }));
    }

    public void EnsureOrganizer(Guid userId)
    {
        if (userId != OrganizerUserId)
        {
            throw new SessionException(SessionError.NotOrganizer);
        }
    }

    /// <summary>Adds a player, or brings the same user back as the same player (rejoin or a second tab).</summary>
    public JoinOutcome Join(Guid userId, string displayName, string connectionId, DateTimeOffset now, SessionRules rules)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(connectionId))
        {
            throw new SessionException(SessionError.Invalid);
        }
        if (State == SessionState.Finished)
        {
            throw new SessionException(SessionError.QuizFinished);
        }
        if (userId == OrganizerUserId)
        {
            throw new SessionException(SessionError.IsOrganizer);
        }

        var player = FindPlayer(userId);
        if (player is not null)
        {
            // A second tab replaces the old connection; the same connection joining again replaces nothing.
            var replaced = player.Online && player.ConnectionId != connectionId ? player.ConnectionId : null;
            player.ConnectionId = connectionId;
            if (!player.Online)
            {
                player.Online = true;
                _events.Add(new PlayerOnlineChanged(userId, player.DisplayName, true));
                if (Phase == SessionPhase.Question && IsEligible(player))
                {
                    RaiseAnswersCount();
                }
            }
            return new JoinOutcome(player, Rejoined: true, replaced, CanAnswer(player, now));
        }

        if (Players.Count >= rules.Capacity)
        {
            throw new SessionException(SessionError.SessionFull);
        }

        player = new Player(QuizId, userId, displayName.Trim(), now) { ConnectionId = connectionId, Online = true };
        Players.Add(player);
        _events.Add(new PlayerJoined(userId, player.DisplayName, Players.Count));
        return new JoinOutcome(player, Rejoined: false, ReplacedConnectionId: null, CanAnswer(player, now));
    }

    public void Start(Guid userId, DateTimeOffset now)
    {
        EnsureOrganizer(userId);
        if (State != SessionState.Lobby)
        {
            throw new SessionException(SessionError.NotInLobby);
        }
        // Starting with nobody online would finish the quiz at once (the all-offline rule).
        if (!Players.Any(p => p.Online))
        {
            throw new SessionException(SessionError.NoPlayers);
        }

        State = SessionState.Running;
        StartQuestion(0, now);
    }

    /// <param name="receivedAt">When the server received the answer (stamped before queueing); decides the deadline and the bonus.</param>
    /// <param name="now">When the actor applies it; used if the answer closes the question.</param>
    public AnswerOutcome SubmitAnswer(Guid userId, string questionId, string optionId, Guid submissionId,
        DateTimeOffset receivedAt, DateTimeOffset now, SessionRules rules)
    {
        if (string.IsNullOrWhiteSpace(questionId) || string.IsNullOrWhiteSpace(optionId) || submissionId == Guid.Empty)
        {
            throw new SessionException(SessionError.Invalid);
        }
        var player = FindPlayer(userId) ?? throw new SessionException(SessionError.NotJoined);
        switch (State)
        {
            case SessionState.Lobby:
                throw new SessionException(SessionError.NotStarted);
            case SessionState.Finished:
                throw new SessionException(SessionError.QuestionClosed);
        }

        var question = CurrentQuestion!;
        if (question.Id != questionId)
        {
            throw new SessionException(SessionError.NotCurrentQuestion);
        }

        var existing = Answers.FirstOrDefault(a => a.UserId == userId && a.QuestionId == questionId);
        if (existing is not null && existing.SubmissionId == submissionId)
        {
            return new AnswerOutcome(questionId, submissionId); // a client retry gets the original result
        }
        if (Phase != SessionPhase.Question || receivedAt >= PhaseEndsAt)
        {
            throw new SessionException(SessionError.QuestionClosed);
        }
        if (existing is not null)
        {
            throw new SessionException(SessionError.AlreadyAnswered);
        }
        if (!IsEligible(player))
        {
            throw new SessionException(SessionError.NotEligible);
        }
        if (!question.HasOption(optionId))
        {
            throw new SessionException(SessionError.Invalid);
        }

        var correct = optionId == question.CorrectOptionId;
        var points = Scoring.Points(correct, PhaseEndsAt!.Value - receivedAt, QuizSet.TimeLimit);
        Answers.Add(new Answer(QuizId, userId, questionId, optionId, submissionId, receivedAt, points));
        if (points > 0)
        {
            player.TotalPoints += points;
            player.ReachedAt = receivedAt;
        }

        var (answered, eligible) = RaiseAnswersCount();
        if (answered == eligible)
        {
            CloseQuestion(now, rules);
        }
        return new AnswerOutcome(questionId, submissionId);
    }

    /// <summary>A connection dropped. Unknown or replaced connections are ignored.</summary>
    public void Disconnect(string connectionId, DateTimeOffset now, SessionRules rules)
    {
        var player = Players.FirstOrDefault(p => p.Online && p.ConnectionId == connectionId);
        if (player is null)
        {
            return;
        }

        player.Online = false;
        player.ConnectionId = null;
        _events.Add(new PlayerOnlineChanged(player.UserId, player.DisplayName, false));

        if (State != SessionState.Running)
        {
            return;
        }
        if (!Players.Any(p => p.Online))
        {
            if (Phase == SessionPhase.Question)
            {
                CloseQuestion(now, rules);
            }
            Finish(now);
            return;
        }
        if (Phase == SessionPhase.Question)
        {
            var (answered, eligible) = AnswerCounts(CurrentQuestion!);
            if (answered == eligible)
            {
                CloseQuestion(now, rules);
            }
            else
            {
                RaiseAnswersCount();
            }
        }
    }

    /// <summary>
    /// The clock: closes a question at its deadline, and starts the next one (or finishes) when
    /// the reveal ends. One transition per call, so a reveal is never skipped. If nobody is
    /// online when the reveal ends, the quiz finishes; after a restart the reveal is the window
    /// in which players can rejoin.
    /// </summary>
    public void Tick(DateTimeOffset now, SessionRules rules)
    {
        if (State != SessionState.Running || now < PhaseEndsAt)
        {
            return;
        }

        if (Phase == SessionPhase.Question)
        {
            CloseQuestion(now, rules);
            return;
        }

        if (!Players.Any(p => p.Online) || CurrentIndex + 1 >= QuizSet.Questions.Count)
        {
            Finish(now);
        }
        else
        {
            StartQuestion(CurrentIndex + 1, now);
        }
    }

    /// <summary>
    /// After a server restart: everyone is offline, and a reveal lasts at least one full reveal
    /// from now, so players have a window to rejoin before the next question (or the finish).
    /// An overdue question is left to the next <see cref="Tick"/>, which closes it and starts a reveal.
    /// </summary>
    public void Resume(DateTimeOffset now, SessionRules rules)
    {
        MarkAllOffline();
        if (State == SessionState.Running && Phase == SessionPhase.Reveal && PhaseEndsAt < now + rules.RevealDuration)
        {
            PhaseEndsAt = now + rules.RevealDuration;
        }
    }

    /// <summary>After a server restart no connection survives: everyone is offline until they rejoin.</summary>
    public void MarkAllOffline()
    {
        foreach (var player in Players)
        {
            player.Online = false;
            player.ConnectionId = null;
        }
    }

    /// <summary>How many eligible players have answered the open question (for the organizer view).</summary>
    public (int Answered, int Eligible) CurrentAnswerCounts() => AnswerCounts(CurrentQuestion!);

    private Player? FindPlayer(Guid userId) => Players.FirstOrDefault(p => p.UserId == userId);

    /// <summary>Online and in the session before the current question started.</summary>
    private bool IsEligible(Player player) => player.Online && player.JoinedAt <= QuestionStartedAt;

    private bool CanAnswer(Player player, DateTimeOffset now) =>
        State == SessionState.Running
        && Phase == SessionPhase.Question
        && now < PhaseEndsAt
        && IsEligible(player)
        && !Answers.Any(a => a.UserId == player.UserId && a.QuestionId == CurrentQuestion!.Id);

    private (int Answered, int Eligible) AnswerCounts(Question question)
    {
        var eligible = Players.Where(IsEligible).ToList();
        var answered = eligible.Count(p => Answers.Any(a => a.UserId == p.UserId && a.QuestionId == question.Id));
        return (answered, eligible.Count);
    }

    /// <summary>Tells the session how many eligible players have answered the current question.</summary>
    private (int Answered, int Eligible) RaiseAnswersCount()
    {
        var question = CurrentQuestion!;
        var counts = AnswerCounts(question);
        _events.Add(new AnswersCountChanged(question.Id, counts.Answered, counts.Eligible));
        return counts;
    }

    private void StartQuestion(int index, DateTimeOffset now)
    {
        CurrentIndex = index;
        Phase = SessionPhase.Question;
        QuestionStartedAt = now;
        PhaseEndsAt = now + QuizSet.TimeLimit;
        _events.Add(new QuestionStarted(CurrentQuestion!.Id, index, QuizSet.Questions.Count, PhaseEndsAt.Value));
    }

    private void CloseQuestion(DateTimeOffset now, SessionRules rules)
    {
        Phase = SessionPhase.Reveal;
        PhaseEndsAt = now + rules.RevealDuration;
        var question = CurrentQuestion!;
        _events.Add(new QuestionClosed(question.Id, question.CorrectOptionId));
    }

    private void Finish(DateTimeOffset now)
    {
        State = SessionState.Finished;
        Phase = SessionPhase.None;
        PhaseEndsAt = null;
        FinishedAt = now;
        _events.Add(new QuizFinished());
    }
}
