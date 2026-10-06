using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>
/// Handles <typeparamref name="TEvent"/> by asking another service for one
/// <typeparamref name="TResponse"/> per item, then applying everything that came back in one go.
/// </summary>
/// <remarks>
/// <para>
/// Found in the assemblies the mediator scans, once the message bus's <c>AddScatterGather</c> is registered.
/// The event is usually a domain event raised
/// by an aggregate: it goes through the outbox, in its own outbox lane, so the outbox marks it
/// processed only after <see cref="GatherAsync"/> has run and its changes have committed. If anything
/// throws, the outbox retries the event. Make it <c>IDedupable</c> to keep one in flight per key.
/// Publishing the event directly through <c>IPublisher</c> also works, but isn't durable.
/// </para>
/// <para>
/// Replies are gathered in memory, so a retry asks again from the start. Don't save in either
/// method: the outbox commits <see cref="GatherAsync"/>'s changes.
/// </para>
/// </remarks>
public interface IScatterGatherHandler<in TEvent, TRequest, TResponse>
    where TEvent : INotification
    where TRequest : class
    where TResponse : class
{
    /// <summary>How long to wait for replies before gathering with whatever has arrived.</summary>
    TimeSpan Timeout { get; }

    /// <summary>The requests to send, one per item key. The key comes back with each reply.</summary>
    Task<IReadOnlyDictionary<string, TRequest>> ScatterAsync(
        TEvent notification,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies the replies: called once, when every item has answered or the timeout has passed.
    /// Throw to have the outbox retry the whole event (e.g. when too much is unanswered).
    /// </summary>
    Task GatherAsync(
        TEvent notification,
        IGathered<TResponse> replies,
        CancellationToken cancellationToken);
}
