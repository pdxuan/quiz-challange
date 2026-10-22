using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Quiz.Server.Domain;
using Quiz.Server.Observability;
using Quiz.Server.Runtime;

namespace Quiz.Server.Features.Play;

/// <summary>
/// The real-time endpoint at /hubs/quiz. Thin: who the caller is comes from the auth cookie, every
/// change goes through the session actor, and a rule failure becomes a <see cref="HubException"/>
/// whose message is only the error code.
/// </summary>
[Authorize]
public sealed class QuizHub(SessionRegistry registry, TimeProvider time, QuizMetrics metrics, ILogger<QuizHub> logger) : Hub
{
    private const string PlayerQuizKey = "playerQuizId";
    private const string GroupsKey = "groups";

    public override Task OnConnectedAsync()
    {
        metrics.Connections.Add(1);
        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        metrics.Connections.Add(-1);
        if (Context.Items.TryGetValue(PlayerQuizKey, out var value) && value is string quizId)
        {
            var connectionId = Context.ConnectionId;
            try
            {
                await registry.AskAsync(quizId, (session, call) => { session.Disconnect(connectionId, call.Now, call.Rules); return 0; });
            }
            catch (Exception ex) when (ex is SessionException or SessionActorException)
            {
                logger.LogWarning("Could not mark connection {ConnectionId} offline in quiz {QuizId}", connectionId, quizId);
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<JoinResult> JoinQuiz(string quizId)
    {
        var id = QuizIdGenerator.Normalize(quizId ?? "");
        var (userId, username) = Caller();
        var connectionId = Context.ConnectionId;

        // Join the group first, so no event between the commit and the reply is missed.
        var added = await AddToGroupsAsync(QuizGroups.Session(id));
        try
        {
            var (result, replaced) = await Call(() => registry.AskAsync(id, (session, call) =>
            {
                var outcome = session.Join(userId, username, connectionId, call.Now, call.Rules);
                var ranked = session.Leaderboard();
                return (new JoinResult(outcome.Player.DisplayName, session.State.ToString(), session.Players.Count,
                    Views.OpenQuestion(session, call.Now), outcome.CanAnswerCurrent, Views.You(ranked, userId), Views.Board(ranked, 10)),
                    outcome.ReplacedConnectionId);
            }));
            if (replaced is not null)
            {
                await Groups.RemoveFromGroupAsync(replaced, QuizGroups.Session(id)); // the old tab stops receiving
            }
            Context.Items[PlayerQuizKey] = id;
            return result;
        }
        catch
        {
            await RemoveFromGroupsAsync(added);
            throw;
        }
    }

    public async Task<WatchSnapshot> WatchQuiz(string quizId)
    {
        var id = QuizIdGenerator.Normalize(quizId ?? "");
        var (userId, _) = Caller();

        // Join the groups first, as in JoinQuiz, so the view misses nothing after its snapshot.
        var added = await AddToGroupsAsync(QuizGroups.Session(id), QuizGroups.Organizer(id));
        try
        {
            return await Call(() => registry.AskAsync(id, (session, call) =>
            {
                session.EnsureOrganizer(userId);
                var counts = session.Phase == SessionPhase.Question ? session.CurrentAnswerCounts() : default((int, int)?);
                return new WatchSnapshot(session.State.ToString(),
                    session.Players.Select(p => new PlayerView(p.DisplayName, p.Online)).ToList(),
                    Views.OpenQuestion(session, call.Now),
                    counts is { } c ? new AnswersCountView(c.Item1, c.Item2) : null,
                    Views.Board(session.Leaderboard()));
            }));
        }
        catch
        {
            await RemoveFromGroupsAsync(added);
            throw;
        }
    }

    public async Task<AnswerAccepted> SubmitAnswer(string quizId, string questionId, string optionId, Guid submissionId)
    {
        var receivedAt = time.GetUtcNow(); // stamped before queueing: waiting in the queue never costs points
        var (userId, _) = Caller();
        try
        {
            var outcome = await Call(() => registry.AskAsync(quizId ?? "", (session, call) =>
                session.SubmitAnswer(userId, questionId ?? "", optionId ?? "", submissionId, receivedAt, call.Now, call.Rules)));
            metrics.AnswersAccepted.Add(1);
            return new AnswerAccepted(outcome.QuestionId, outcome.SubmissionId);
        }
        catch (HubException ex)
        {
            metrics.AnswersRejected.Add(1, new KeyValuePair<string, object?>("code", ex.Message));
            throw;
        }
    }

    /// <summary>
    /// Adds the connection to the groups (always: another tab may have removed it) and returns the
    /// ones it was not known to be in, so a failed call removes only what it added.
    /// </summary>
    private async Task<List<string>> AddToGroupsAsync(params string[] groups)
    {
        var added = groups.Where(MyGroups().Add).ToList();
        foreach (var group in groups)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
        }
        return added;
    }

    private async Task RemoveFromGroupsAsync(List<string> groups)
    {
        var mine = MyGroups();
        foreach (var group in groups)
        {
            mine.Remove(group);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        }
    }

    /// <summary>The groups this connection is in (SignalR does not expose them).</summary>
    private HashSet<string> MyGroups()
    {
        if (Context.Items.TryGetValue(GroupsKey, out var value) && value is HashSet<string> groups)
        {
            return groups;
        }
        var created = new HashSet<string>();
        Context.Items[GroupsKey] = created;
        return created;
    }

    private (Guid UserId, string Username) Caller() =>
        (Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!), Context.User!.Identity!.Name!);

    /// <summary>Maps failures to a code-only <see cref="HubException"/>; no exception text reaches the client.</summary>
    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (SessionException ex)
        {
            throw new HubException(ex.Error.ToString());
        }
        catch (SessionActorException)
        {
            throw new HubException("ServerError");
        }
    }
}
