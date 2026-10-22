using System.Diagnostics.Metrics;

namespace Quiz.Server.Observability;

/// <summary>
/// The instruments under the "Quiz.Server" meter (view live with
/// <c>dotnet-counters monitor -n Quiz.Server --counters Quiz.Server</c>). Recorded from the
/// session actor and the hub.
/// </summary>
public sealed class QuizMetrics
{
    public const string MeterName = "Quiz.Server";

    public QuizMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        LiveSessions = meter.CreateUpDownCounter<int>("quiz.sessions.live", description: "Sessions with a running actor");
        Connections = meter.CreateUpDownCounter<int>("quiz.connections", description: "Open hub connections");
        AnswersAccepted = meter.CreateCounter<long>("quiz.answers.accepted", description: "Answers stored");
        AnswersRejected = meter.CreateCounter<long>("quiz.answers.rejected", description: "Answers rejected, tagged with the error code");
        MessageDuration = meter.CreateHistogram<double>("quiz.actor.message.duration", unit: "ms", description: "Actor message handling time, including the database");
        ClosePushDuration = meter.CreateHistogram<double>("quiz.question.close_push.duration", unit: "ms", description: "Time from question close to results handed to SignalR");
    }

    public UpDownCounter<int> LiveSessions { get; }
    public UpDownCounter<int> Connections { get; }
    public Counter<long> AnswersAccepted { get; }
    public Counter<long> AnswersRejected { get; }
    public Histogram<double> MessageDuration { get; }
    public Histogram<double> ClosePushDuration { get; }
}
