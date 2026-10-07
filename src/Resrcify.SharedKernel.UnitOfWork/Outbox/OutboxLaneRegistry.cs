using System;
using System.Collections.Generic;
using System.Linq;

using Resrcify.SharedKernel.Abstractions.UnitOfWork;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// Which event types each outbox lane takes, from the registered <see cref="IOutboxLaneEvent"/>s. An event type is in
/// one lane only: in two, each lane would process the same message (two publishes, two scatter-gathers). A service's own
/// <see cref="OutboxLaneEvent"/> moves a type out of a package's lane (e.g. scatter-gather's); two lanes of the service's
/// own, or of packages, for one type are a mistake, refused when the lanes start.
/// </summary>
internal sealed class OutboxLaneRegistry
{
    /// <param name="laneEvents">Lane events registered as <see cref="IOutboxLaneEvent"/>.</param>
    /// <param name="concreteLaneEvents">Lane events registered as <see cref="OutboxLaneEvent"/> itself, so they count too.</param>
    /// <exception cref="InvalidOperationException">An event type is in two lanes.</exception>
    public OutboxLaneRegistry(IEnumerable<IOutboxLaneEvent> laneEvents, IEnumerable<OutboxLaneEvent> concreteLaneEvents)
    {
        Lanes = laneEvents
            .Concat(concreteLaneEvents)
            .GroupBy(laneEvent => laneEvent.EventType)
            .Select(OneLane)
            .GroupBy(laneEvent => laneEvent.Lane, StringComparer.Ordinal)
            .ToDictionary(
                lane => lane.Key,
                lane => (IReadOnlyList<string>)lane.Select(laneEvent => laneEvent.EventType.FullName!).Distinct().ToList(),
                StringComparer.Ordinal);
        LaneEventTypes = Lanes.Values.SelectMany(types => types).Distinct().ToList();
    }

    private static IOutboxLaneEvent OneLane(IGrouping<Type, IOutboxLaneEvent> registrations)
    {
        var lanes = registrations.DistinctBy(laneEvent => laneEvent.Lane, StringComparer.Ordinal).ToList();
        if (lanes.Count == 1)
            return lanes[0];

        var own = lanes.Where(laneEvent => laneEvent is OutboxLaneEvent).ToList();
        if (own.Count == 1)
            return own[0];

        throw new InvalidOperationException(
            $"{registrations.Key.Name} is in more than one outbox lane ({string.Join(", ", lanes.Select(laneEvent => laneEvent.Lane))}), "
            + "so each would process its messages. Put it in one lane.");
    }

    /// <summary>Each lane's event type names, as stored in the outbox's <c>Type</c> column.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Lanes { get; }

    /// <summary>Every event type that belongs to some lane: the regular job skips these.</summary>
    public IReadOnlyList<string> LaneEventTypes { get; }
}
