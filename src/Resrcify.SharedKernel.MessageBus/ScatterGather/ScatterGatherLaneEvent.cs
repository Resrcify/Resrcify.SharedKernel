using System;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>
/// Puts an event with a scatter-gather handler in the outbox's <see cref="LaneName"/> lane, so a batch waiting for
/// replies never holds up the regular outbox.
/// </summary>
internal sealed record ScatterGatherLaneEvent(Type EventType) : IOutboxLaneEvent
{
    /// <summary>The outbox lane for events whose handler waits for replies from other services.</summary>
    public const string LaneName = "scatter-gather";

    public string Lane => LaneName;
}
