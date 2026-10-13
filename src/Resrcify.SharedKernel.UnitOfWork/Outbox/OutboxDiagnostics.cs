using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// Tracing and metrics for outbox processing.
/// <para>
/// Tracing: one span per processed message, by the regular job and by every lane. Whatever the event's handlers do
/// runs inside it, so their spans (database, HTTP, message bus sends) form one trace per message. Subscribe with
/// <c>.AddSource(OutboxDiagnostics.ActivitySourceName)</c>.
/// </para>
/// <para>
/// Metrics (<c>.AddMeter(OutboxDiagnostics.MeterName)</c>), each tagged with the DbContext (<c>context</c>):
/// <list type="bullet">
/// <item><c>outbox.messages.handled</c>: messages handled, by <c>event</c> and <c>outcome</c> (<c>processed</c>,
/// <c>retrying</c>: failed and tried again later, <c>gave_up</c>: failed its last try and is kept, marked given up).</item>
/// <item><c>outbox.message.duration</c> (s): how long handling a message took, by <c>event</c> and <c>outcome</c>.</item>
/// <item><c>outbox.message.wait</c> (s): how long a processed message waited in the outbox, by <c>event</c>.</item>
/// <item><c>outbox.messages.waiting</c>, <c>outbox.messages.poison</c> and <c>outbox.oldest_waiting.age</c> (s): the
/// backlog, measured every 30 s by the backlog monitor.</item>
/// </list>
/// </para>
/// </summary>
public static class OutboxDiagnostics
{
    public const string ActivitySourceName = "Resrcify.SharedKernel.UnitOfWork.Outbox";

    public const string MeterName = "Resrcify.SharedKernel.UnitOfWork.Outbox";

    internal static ActivitySource ActivitySource { get; } = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Handled = Meter.CreateCounter<long>(
        "outbox.messages.handled",
        description: "Outbox messages handled, by outcome: processed, retrying (failed, tried again later) or gave_up (failed its last try).");

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "outbox.message.duration",
        unit: "s",
        description: "How long handling an outbox message took.");

    private static readonly Histogram<double> Wait = Meter.CreateHistogram<double>(
        "outbox.message.wait",
        unit: "s",
        description: "How long a processed outbox message waited in the outbox.");

    /// <summary>The backlog monitors, by DbContext; the gauges read their last measurement.</summary>
    private static readonly ConcurrentDictionary<string, Func<OutboxBacklog?>> Backlogs = new(StringComparer.Ordinal);

    static OutboxDiagnostics()
    {
        Meter.CreateObservableGauge(
            "outbox.messages.waiting",
            () => Observe(backlog => backlog.Waiting),
            description: "Outbox messages waiting to be processed (not given up).");
        Meter.CreateObservableGauge(
            "outbox.messages.poison",
            () => Observe(backlog => backlog.Poison),
            description: "Outbox messages that gave up (failed their last try).");
        Meter.CreateObservableGauge(
            "outbox.oldest_waiting.age",
            () => Observe(backlog => backlog.OldestWaitingAge.TotalSeconds),
            unit: "s",
            description: "How long the oldest waiting outbox message has waited (0 when none waits).");
    }

    internal static void RecordHandled(
        string context,
        string eventName,
        string outcome,
        TimeSpan duration,
        TimeSpan? wait)
    {
        var tags = new TagList
        {
            { "context", context },
            { "event", eventName },
            { "outcome", outcome },
        };
        Handled.Add(1, tags);
        Duration.Record(duration.TotalSeconds, tags);
        if (wait is { } waited)
            Wait.Record(Math.Max(0, waited.TotalSeconds), new TagList { { "context", context }, { "event", eventName } });
    }

    /// <summary>The short name of a stored event type (<c>Shard.Domain.Events.RankChanged</c> → <c>RankChanged</c>).</summary>
    internal static string EventName(string storedType)
    {
        var lastDot = storedType.LastIndexOf('.');
        return lastDot < 0 ? storedType : storedType[(lastDot + 1)..];
    }

    internal static void TrackBacklog(string context, Func<OutboxBacklog?> latest)
        => Backlogs[context] = latest;

    internal static void StopTrackingBacklog(string context)
        => Backlogs.TryRemove(context, out _);

    private static IEnumerable<Measurement<T>> Observe<T>(Func<OutboxBacklog, T> read)
        where T : struct
    {
        foreach (var (context, latest) in Backlogs)
            if (latest() is { } backlog)
                yield return new Measurement<T>(read(backlog), new KeyValuePair<string, object?>("context", context));
    }
}

/// <summary>The outbox's backlog when it was last measured.</summary>
/// <param name="Waiting">Unprocessed messages that will be tried (again).</param>
/// <param name="Poison">Messages that failed their last try (marked given up, or unprocessed and out of tries).</param>
/// <param name="OldestWaitingAge">How long the oldest waiting message had waited; zero when none waits.</param>
/// <param name="MeasuredAt">When it was measured.</param>
/// <param name="WaitingForLaterTry">Of the waiting, those a lane tries again later (their <c>NextAttemptOnUtc</c> is ahead).</param>
internal sealed record OutboxBacklog(
    long Waiting,
    long Poison,
    TimeSpan OldestWaitingAge,
    DateTimeOffset MeasuredAt,
    long WaitingForLaterTry = 0);
