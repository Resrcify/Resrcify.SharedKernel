using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Bus;
using Rebus.Messages;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Diagnostics;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>
/// Publishes on the service's bus to the topic named by the event's wire name, so subscribers bind by name
/// rather than by class (each side can have its own class for the event).
/// </summary>
/// <remarks>
/// <para>
/// Published while the outbox handles a domain event, an event's message ID comes from the outbox message, its name
/// and its content (<see cref="IOutboxMessageContext.NextStableId"/>): a retried outbox message publishes it again
/// under the same ID, so subscribers that skip duplicates (<c>SkipDuplicateEvents</c>) handle it once. A redelivery
/// keeps its ID anyway. An event whose content is computed while handling (a time from the clock) differs on a retry,
/// and so gets a new ID: give it <see cref="IDedupable"/> (its subject, e.g. the shard id), and its ID comes from that
/// key instead of its content.
/// </para>
/// <para>The topic is the event's own wire name (its runtime type's), however the caller declared it.</para>
/// <para>Every event carries its publisher's service name, so subscribers can tell when two services publish an event of the same name.</para>
/// </remarks>
internal sealed class EventBus(IBus bus, MessageBusSettings settings, IOutboxMessageContext? outboxMessage = null) : IEventBus
{
    /// <summary>The header naming the service that published an event.</summary>
    public const string PublisherHeader = "resrcify-publisher";

    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        cancellationToken.ThrowIfCancellationRequested();
        // The runtime type, as Rebus names the message: published through a base class or an interface, the event
        // would otherwise go to a topic nobody binds and be dropped.
        var topic = settings.WireNameOf(integrationEvent.GetType());
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PublisherHeader] = settings.ServiceName,
        };
        if (outboxMessage?.MessageId is not null
            && outboxMessage.NextStableId(StableIdKind(topic, integrationEvent)) is { } stableId)
            headers[Headers.MessageId] = stableId.ToString("D");
        await bus.Advanced.Topics.Publish(topic, integrationEvent, headers);
        MessageBusDiagnostics.RecordPublished(topic, viaOutbox: outboxMessage?.MessageId is not null);
    }

    // The topic plus a fingerprint of the content: a handler publishing several events of one name gets each its
    // own ID however they interleave (published in parallel, their order changes from try to try), and only events
    // with the same content share a counter, where a swap doesn't matter. An event that names its subject
    // (IDedupable) is fingerprinted by that instead, so content computed while handling doesn't change its ID.
    internal static string StableIdKind(string topic, object integrationEvent)
    {
        if (integrationEvent is IDedupable dedupable)
            return $"{topic}@{dedupable.DedupKey}";

        byte[] content;
        try
        {
            content = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, integrationEvent.GetType());
        }
        catch (Exception exception) when (exception is NotSupportedException or JsonException or InvalidOperationException)
        {
            return topic;   // content System.Text.Json can't write: the publish order alone tells the events apart
        }

        return $"{topic}#{Convert.ToHexString(SHA256.HashData(content))}";
    }
}
