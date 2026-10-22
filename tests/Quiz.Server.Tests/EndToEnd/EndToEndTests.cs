using System.Diagnostics;
using Quiz.Server.Tests.TestHost;
using Xunit.Abstractions;

namespace Quiz.Server.Tests.EndToEnd;

/// <summary>
/// Whole quizzes over the public boundary (REST + SignalR) on a real SQLite file. Random choices
/// come from a fixed seed, so every run plays the same game; the clock is the test's fake clock.
/// </summary>
public class EndToEndTests(ITestOutputHelper output)
{
    private const int Players = 20;
    private const int Questions = 5;

    [Fact]
    public async Task FullQuiz_TwentyPlayersWithLateAnswersAndARejoin_FinalTotalsAndRanksMatchTheAnswerRows()
    {
        var watch = Stopwatch.StartNew();
        using var factory = new QuizApiFactory();
        var random = new Random(35);
        await using var session = await E2ESession.OpenAsync(factory, "e2e", Players, Questions);
        await session.StartAsync();

        var rejoiner = session.Names[4];
        for (var index = 0; index < Questions; index++)
        {
            await E2ESession.PlayQuestionAsync(factory, [session], index, random,
                afterStart: index == 1 ? (s, questionId) => s.DisconnectAndRejoinAsync(rejoiner, questionId) : null,
                mustAnswer: index == 1 ? new HashSet<string> { rejoiner } : null);
        }

        await session.VerifyFinishedAsync();
        Assert.Contains(session.Accepted, a => a.Player == rejoiner && a.QuestionId == session.Set.Ordered[1].Id); // answered after the rejoin
        Assert.True(session.LateRejected >= Questions, $"only {session.LateRejected} late answers");
        session.VerifyNoForeignEvents();
        E2ESession.AssertNoUnexpectedErrorsLogged(factory, session.LateRejected);
        output.WriteLine($"AC35: 1 session x {Players} players x {Questions} questions, {session.Accepted.Count} answers accepted, " +
                         $"{session.LateRejected} late rejected, wall time {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task ThreeSessionsAtOnce_TwentyPlayersEach_RunToTheEndWithNoCrossSessionEvents()
    {
        var watch = Stopwatch.StartNew();
        using var factory = new QuizApiFactory();
        var random = new Random(36);
        var sessions = await Task.WhenAll(new[] { "s1", "s2", "s3" }.Select(prefix => E2ESession.OpenAsync(factory, prefix, Players, Questions)));
        try
        {
            var setup = watch.ElapsedMilliseconds;
            await Task.WhenAll(sessions.Select(s => s.StartAsync()));

            for (var index = 0; index < Questions; index++)
            {
                await E2ESession.PlayQuestionAsync(factory, sessions, index, random);
            }

            await Task.WhenAll(sessions.Select(s => s.VerifyFinishedAsync()));
            Assert.Equal(3, sessions.Select(s => s.QuizId).Distinct().Count());
            Assert.All(sessions, s => s.VerifyNoForeignEvents());
            E2ESession.AssertNoUnexpectedErrorsLogged(factory, sessions.Sum(s => s.LateRejected));
            output.WriteLine($"AC36: 3 sessions x {Players} players x {Questions} questions, " +
                             $"{sessions.Sum(s => s.Accepted.Count)} answers accepted, {sessions.Sum(s => s.LateRejected)} late rejected, " +
                             $"setup (log in, join) {setup} ms, wall time {watch.ElapsedMilliseconds} ms");
        }
        finally
        {
            foreach (var s in sessions)
            {
                await s.DisposeAsync();
            }
        }
    }
}
