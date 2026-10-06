using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;

/// <summary>Records when the outbox handed each shard's events to their handlers.</summary>
internal sealed class EventTracker
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<long>> _handled = new();

    /// <summary>Completes with the timestamp (<see cref="TimeProvider.GetTimestamp"/>) at which <paramref name="shardId"/>'s event was handled.</summary>
    public Task<long> HandledAsync(Guid shardId)
        => Source(shardId).Task;

    public void Record(Guid shardId)
        => Source(shardId).TrySetResult(TimeProvider.System.GetTimestamp());

    private TaskCompletionSource<long> Source(Guid shardId)
        => _handled.GetOrAdd(shardId, _ => new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously));
}

internal sealed class ShardRenamedHandler(EventTracker tracker)
    : INotificationHandler<ShardRenamed>
{
    public Task Handle(
        ShardRenamed notification,
        CancellationToken cancellationToken)
    {
        tracker.Record(notification.ShardId);
        return Task.CompletedTask;
    }
}

internal sealed class RanksRequestedHandler(EventTracker tracker)
    : INotificationHandler<RanksRequested>
{
    public Task Handle(
        RanksRequested notification,
        CancellationToken cancellationToken)
    {
        tracker.Record(notification.ShardId);
        return Task.CompletedTask;
    }
}
