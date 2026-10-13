using System;

namespace Resrcify.SharedKernel.Abstractions.UnitOfWork;

/// <summary>
/// Puts a domain event type in a named outbox lane. Register one per event type as a singleton
/// (<c>services.AddSingleton&lt;IOutboxLaneEvent&gt;(...)</c>). The regular outbox job then skips that type, and the lane
/// processes it on its own, with its own concurrency, so its messages and the regular ones never hold each other up.
/// </summary>
/// <remarks>The message bus registers one for every event with a scatter-gather handler, so those need none.</remarks>
public interface IOutboxLaneEvent
{
    /// <summary>The lane's name.</summary>
    string Lane { get; }

    /// <summary>The domain event type the lane takes.</summary>
    Type EventType { get; }
}
