using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using Resrcify.SharedKernel.MessageBus.PublishSubscribe;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Resrcify.SharedKernel.MessageBus.ScatterGather;

namespace Resrcify.SharedKernel.MessageBus.Diagnostics;

/// <summary>
/// The message bus' own metrics, next to Rebus' (<c>Rebus.Diagnostics</c>: message counts, sizes and delays).
/// Subscribe with <c>.AddMeter(MessageBusDiagnostics.MeterName)</c>.
/// </summary>
/// <remarks>
/// <para><b>Requester</b> (scatter-gather), tagged with the request's wire name (<c>message</c>):</para>
/// <list type="bullet">
/// <item><c>messagebus.scatter_gather.batch.duration</c> (s), by <c>end</c>: <c>complete</c> (every item answered),
/// <c>timeout</c>, or <c>stopped</c> (a streaming caller stopped reading).</item>
/// <item><c>messagebus.scatter_gather.items</c>, by <c>outcome</c>: <c>answered</c>, <c>failed</c> (a failure that is
/// the request's fault, e.g. not found), <c>gave_up</c> (the responder kept failing until its last try) or
/// <c>unanswered</c>.</item>
/// <item><c>messagebus.scatter_gather.replies</c>, by <c>outcome</c>: <c>accepted</c>, <c>duplicate</c> (delivery is
/// at-least-once), <c>late</c> (after its batch ended) or <c>unreadable</c> (dropped). Only accepted ones count.</item>
/// </list>
/// <para><b>Responder</b> (rate-limited queues), tagged with the <c>queue</c>:</para>
/// <list type="bullet">
/// <item><c>messagebus.requests.handled</c> and <c>messagebus.requests.duration</c> (s), by <c>outcome</c>:
/// <c>success</c>, <c>failure</c> (answered with errors that are the request's fault), <c>retried</c> (failed in a way
/// another try may fix, or threw: sent back to the queue), <c>gave_up</c> (still failing on its last try: answered with
/// its errors), <c>expired</c> (its
/// requester had stopped waiting: not answered) or <c>cancelled</c> (the queue stopped mid-request: back to the queue).</item>
/// <item><c>messagebus.requests.dropped</c>: requests the bus itself couldn't handle (unreadable, or cancelled on every
/// try) and dropped.</item>
/// <item><c>messagebus.rate_limiter.wait</c> (s): how long a request waited for the queue's rate limit.</item>
/// <item><c>messagebus.queue.consuming</c>: 1 while this instance consumes the queue, 0 while it has stepped aside
/// (its health check is unhealthy).</item>
/// </list>
/// <para><b>Publish/subscribe</b>, tagged with the event's wire name (<c>event</c>):</para>
/// <list type="bullet">
/// <item><c>messagebus.events.published</c>.</item>
/// <item><c>messagebus.events.handled</c> and <c>messagebus.events.duration</c> (s), by <c>outcome</c>:
/// <c>success</c>, <c>rejected</c> (a failure that is the event's fault: not retried), <c>error</c> (retried, then moved
/// to the error queue) or <c>duplicate</c> (already handled: skipped).</item>
/// </list>
/// <para><b>Failures</b>, tagged with the message's wire name (<c>message</c>):</para>
/// <list type="bullet">
/// <item><c>messagebus.messages.dead_lettered</c>, also by <c>queue</c>: messages that kept failing and were moved to an
/// error queue. Alert on it: someone has to look at them.</item>
/// <item><c>messagebus.messages.unknown</c>: messages of a type this service doesn't receive (e.g. an event whose
/// handler was deleted while the subscription stayed: see <c>RemoveSubscription</c>).</item>
/// <item><c>messagebus.events.name_clashes</c>, by <c>event</c>: events of the same name arriving from two services
/// (names are global on the broker); prefix event class names with their owner.</item>
/// </list>
/// </remarks>
public static class MessageBusDiagnostics
{
    public const string MeterName = "Resrcify.SharedKernel.MessageBus";

    private static readonly Meter Meter = new(MeterName);

    private static readonly ConcurrentDictionary<IQueueConsumer, byte> Consumers = new();

    private static readonly Counter<long> ScatterGatherReplies = Meter.CreateCounter<long>(
        "messagebus.scatter_gather.replies",
        unit: "{reply}",
        description: "Scatter-gather replies received, by outcome: accepted, duplicate or late (both ignored).");

    private static readonly Counter<long> ScatterGatherItems = Meter.CreateCounter<long>(
        "messagebus.scatter_gather.items",
        unit: "{item}",
        description: "Scatter-gather items by how they ended: answered, failed or unanswered.");

    private static readonly Histogram<double> BatchDuration = Meter.CreateHistogram<double>(
        "messagebus.scatter_gather.batch.duration",
        unit: "s",
        description: "From sending a batch's requests to its end: complete, timeout or stopped.");

    private static readonly Counter<long> RequestsHandled = Meter.CreateCounter<long>(
        "messagebus.requests.handled",
        unit: "{request}",
        description: "Requests handled on a rate-limited queue, by outcome.");

    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "messagebus.requests.duration",
        unit: "s",
        description: "How long the handler took, by outcome (excludes the rate-limit wait).");

    private static readonly Counter<long> RequestsDropped = Meter.CreateCounter<long>(
        "messagebus.requests.dropped",
        unit: "{request}",
        description: "Requests the bus couldn't handle on a rate-limited queue (unreadable, or cancelled on every try), dropped.");

    private static readonly Histogram<double> RateLimiterWait = Meter.CreateHistogram<double>(
        "messagebus.rate_limiter.wait",
        unit: "s",
        description: "How long a request waited for its queue's rate limit.");

    private static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>(
        "messagebus.events.published",
        unit: "{event}",
        description: "Integration events published.");

    private static readonly Counter<long> EventsHandled = Meter.CreateCounter<long>(
        "messagebus.events.handled",
        unit: "{event}",
        description: "Integration events handled, by outcome: success, rejected, error or duplicate (skipped).");

    private static readonly Counter<long> MessagesDeadLettered = Meter.CreateCounter<long>(
        "messagebus.messages.dead_lettered",
        unit: "{message}",
        description: "Messages that kept failing and were moved to an error queue.");

    private static readonly Counter<long> MessagesUnknown = Meter.CreateCounter<long>(
        "messagebus.messages.unknown",
        unit: "{message}",
        description: "Messages of a type this service doesn't receive.");

    private static readonly Counter<long> EventNameClashes = Meter.CreateCounter<long>(
        "messagebus.events.name_clashes",
        unit: "{event}",
        description: "Events of the same name arriving from two different services.");

    private static readonly Histogram<double> EventDuration = Meter.CreateHistogram<double>(
        "messagebus.events.duration",
        unit: "s",
        description: "How long an integration event's handlers took, by outcome.");

    private static readonly ObservableGauge<int> QueueConsuming = Meter.CreateObservableGauge(
        "messagebus.queue.consuming",
        () => Consumers.Keys.Select(consumer => new Measurement<int>(
            consumer.IsConsuming ? 1 : 0,
            new KeyValuePair<string, object?>("queue", consumer.QueueName))),
        unit: "{queue}",
        description: "1 while this instance consumes the queue, 0 while it has stepped aside.");

    /// <summary>The gauge observes the tracked consumers; referenced so it is created with the meter.</summary>
    internal static string QueueConsumingName => QueueConsuming.Name;

    internal static void Track(IQueueConsumer consumer)
        => Consumers.TryAdd(consumer, 0);

    internal static void Untrack(IQueueConsumer consumer)
        => Consumers.TryRemove(consumer, out _);

    internal static void RecordReply(ReplyOutcome outcome)
        => ScatterGatherReplies.Add(1, Tag("outcome", outcome switch
        {
            ReplyOutcome.Accepted => "accepted",
            ReplyOutcome.Duplicate => "duplicate",
            ReplyOutcome.Unreadable => "unreadable",
            _ => "late",
        }));

    internal static void RecordBatch(string message, BatchItems items, BatchEnd end, TimeSpan duration)
    {
        var messageTag = Tag("message", message);
        AddItems(items.Answered, messageTag, "answered");
        AddItems(items.Failed, messageTag, "failed");
        AddItems(items.GaveUp, messageTag, "gave_up");
        AddItems(items.Unanswered, messageTag, "unanswered");
        BatchDuration.Record(duration.TotalSeconds, messageTag, Tag("end", end switch
        {
            BatchEnd.Complete => "complete",
            BatchEnd.Timeout => "timeout",
            _ => "stopped",
        }));
    }

    internal static void RecordRequest(string queue, RequestOutcome outcome, TimeSpan duration)
    {
        var queueTag = Tag("queue", queue);
        var outcomeTag = Tag("outcome", outcome switch
        {
            RequestOutcome.Success => "success",
            RequestOutcome.Failure => "failure",
            RequestOutcome.Retried => "retried",
            RequestOutcome.GaveUp => "gave_up",
            RequestOutcome.Expired => "expired",
            _ => "cancelled",
        });
        RequestsHandled.Add(1, queueTag, outcomeTag);
        RequestDuration.Record(duration.TotalSeconds, queueTag, outcomeTag);
    }

    internal static void RecordDropped(string queue)
        => RequestsDropped.Add(1, Tag("queue", queue));

    internal static void RecordRateLimitWait(string queue, TimeSpan wait)
        => RateLimiterWait.Record(wait.TotalSeconds, Tag("queue", queue));

    /// <param name="viaOutbox">
    /// Whether it was published while the outbox processed a message: if the publish fails, the outbox retries it.
    /// Published anywhere else (a command handler, a callback), a failed publish is lost.
    /// </param>
    internal static void RecordPublished(string integrationEvent, bool viaOutbox)
        => EventsPublished.Add(1, Tag("event", integrationEvent), Tag("via_outbox", viaOutbox ? "true" : "false"));

    internal static void RecordEvent(string integrationEvent, EventOutcome outcome, TimeSpan duration)
    {
        var eventTag = Tag("event", integrationEvent);
        var outcomeTag = Tag("outcome", outcome switch
        {
            EventOutcome.Success => "success",
            EventOutcome.Duplicate => "duplicate",
            EventOutcome.Rejected => "rejected",
            _ => "error",
        });
        EventsHandled.Add(1, eventTag, outcomeTag);
        EventDuration.Record(duration.TotalSeconds, eventTag, outcomeTag);
    }

    internal static void RecordDeadLettered(string queue, string message)
        => MessagesDeadLettered.Add(1, Tag("queue", queue), Tag("message", message));

    internal static void RecordNameClash(string integrationEvent, string firstPublisher, string otherPublisher)
        => EventNameClashes.Add(1, Tag("event", integrationEvent), Tag("publishers", $"{firstPublisher},{otherPublisher}"));

    internal static void RecordUnknown(string message)
        => MessagesUnknown.Add(1, Tag("message", message));

    private static void AddItems(int count, KeyValuePair<string, object?> messageTag, string outcome)
    {
        if (count > 0)
            ScatterGatherItems.Add(count, messageTag, Tag("outcome", outcome));
    }

    private static KeyValuePair<string, object?> Tag(string name, string value)
        => new(name, value);
}
