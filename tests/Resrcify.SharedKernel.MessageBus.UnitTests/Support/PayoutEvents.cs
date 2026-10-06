using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Support;

/// <summary>An integration event for the publish/subscribe tests; every subscribing test host handles it.</summary>
internal sealed record PayoutRotated(string ShardId, int Sequence);

/// <summary>What a subscribing host's <see cref="PayoutRotatedHandler"/> saw, and how it should behave.</summary>
internal sealed class PayoutLog
{
    private int _attempts;

    public ConcurrentQueue<PayoutRotated> Handled { get; } = new();

    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>How many of the first attempts throw.</summary>
    public int FailFirstAttempts { get; init; }

    public bool AlwaysFail { get; init; }

    /// <summary>What every attempt waits for before it is handled (nothing by default).</summary>
    public Task Hold { get; init; } = Task.CompletedTask;

    public void Handle(PayoutRotated payout)
    {
        var attempt = Interlocked.Increment(ref _attempts);
        if (AlwaysFail || attempt <= FailFirstAttempts)
            throw new InvalidOperationException($"Attempt {attempt} fails.");
        Handled.Enqueue(payout);
    }
}

internal sealed class PayoutRotatedHandler(PayoutLog log) : IIntegrationEventHandler<PayoutRotated>
{
    // Fails by throwing (PayoutLog): an exception is handled like a failed result.
    public async Task<Result> HandleAsync(PayoutRotated integrationEvent, CancellationToken cancellationToken)
    {
        await log.Hold.WaitAsync(cancellationToken);
        log.Handle(integrationEvent);
        return Result.Success();
    }
}
