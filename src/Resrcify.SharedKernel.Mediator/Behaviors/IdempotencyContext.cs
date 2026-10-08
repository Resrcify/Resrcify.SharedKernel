using System.Collections.Generic;
using System.Threading;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>
/// The requests of this scope answered with an earlier result (scoped, registered with the mediator). Marked per request
/// instance, so a request a handler sends in turn doesn't mark the one it was sent for.
/// </summary>
internal sealed class IdempotencyContext : IIdempotencyContext
{
    private readonly Lock _gate = new();
    private readonly HashSet<object> _replayed = new(ReferenceEqualityComparer.Instance);

    public bool WasReplayed(object request)
    {
        lock (_gate)
            return _replayed.Contains(request);
    }

    internal void MarkReplayed(object request)
    {
        lock (_gate)
            _replayed.Add(request);
    }
}
