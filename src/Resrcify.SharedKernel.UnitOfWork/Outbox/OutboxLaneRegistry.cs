using System;
using System.Collections.Generic;
using System.Linq;

using Resrcify.SharedKernel.Abstractions.UnitOfWork;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// Which event types each outbox lane takes, from the registered <see cref="IOutboxLaneEvent"/>s.
/// </summary>
internal sealed class OutboxLaneRegistry
{
    /// <param name="laneEvents">Lane events registered as <see cref="IOutboxLaneEvent"/>.</param>
    /// <param name="concreteLaneEvents">Lane events registered as <see cref="OutboxLaneEvent"/> itself, so they count too.</param>
    public OutboxLaneRegistry(IEnumerable<IOutboxLaneEvent> laneEvents, IEnumerable<OutboxLaneEvent> concreteLaneEvents)
    {
        Lanes = laneEvents
            .Concat(concreteLaneEvents)
            .GroupBy(laneEvent => laneEvent.Lane, StringComparer.Ordinal)
            .ToDictionary(
                lane => lane.Key,
                lane => (IReadOnlyList<string>)lane.Select(laneEvent => laneEvent.EventType.FullName!).Distinct().ToList(),
                StringComparer.Ordinal);
        LaneEventTypes = Lanes.Values.SelectMany(types => types).Distinct().ToList();
    }

    /// <summary>Each lane's event type names, as stored in the outbox's <c>Type</c> column.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Lanes { get; }

    /// <summary>Every event type that belongs to some lane: the regular job skips these.</summary>
    public IReadOnlyList<string> LaneEventTypes { get; }
}
