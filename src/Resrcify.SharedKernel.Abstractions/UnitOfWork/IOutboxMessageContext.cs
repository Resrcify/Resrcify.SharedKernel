using System;

namespace Resrcify.SharedKernel.Abstractions.UnitOfWork;

/// <summary>
/// The outbox message whose domain event is being handled right now, if any. Work done while handling it can take
/// <see cref="NextStableId"/>s: the same IDs every time the message is retried, so whoever receives what the handler
/// sent out can recognise a repeat (e.g. an integration event published again after a failed attempt).
/// </summary>
/// <remarks>Registered by the outbox (<c>AddOutboxProcessing</c>, <c>AddOutboxLanes</c>, the outbox job).</remarks>
public interface IOutboxMessageContext
{
    /// <summary>The outbox message being handled, or <see langword="null"/> outside outbox processing.</summary>
    Guid? MessageId { get; }

    /// <summary>
    /// An ID for the next thing of kind <paramref name="kind"/> this message's handling produces: the n-th one of a
    /// kind gets the same ID on every attempt, as long as the handlers produce them in the same order.
    /// <see langword="null"/> outside outbox processing.
    /// </summary>
    /// <param name="kind">What the ID is for, e.g. an event's wire name; kinds are counted separately.</param>
    Guid? NextStableId(string kind);
}
