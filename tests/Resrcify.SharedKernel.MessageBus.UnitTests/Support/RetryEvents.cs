using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Support;

/// <summary>An event whose handler fails its first try: what does the second try see, and what goes out?</summary>
internal sealed record RetriedEvent(string Id);

/// <summary>What <see cref="RetriedEventHandler"/> publishes on every try before it decides how the try ends.</summary>
internal sealed record SideEffectPublished(string Id);

/// <summary>Work a try tracks in its DI scope, as a DbContext does: a try that sees work left over saves it too.</summary>
internal sealed class TryWork
{
    public int Changes { get; set; }
}

/// <summary>What the retry handlers saw.</summary>
internal sealed class RetryLog
{
    private int _attempts;

    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>The changes the scope held when the successful try ended (1 when each try has a scope of its own).</summary>
    public int ChangesSeenBySuccess { get; set; }

    public ConcurrentQueue<SideEffectPublished> SideEffects { get; } = new();

    public int NextAttempt()
        => Interlocked.Increment(ref _attempts);
}

/// <summary>Changes its scope's work and publishes, then fails the first try.</summary>
internal sealed class RetriedEventHandler(RetryLog log, TryWork work, IEventBus events)
    : IIntegrationEventHandler<RetriedEvent>
{
    public async Task<Result> HandleAsync(RetriedEvent integrationEvent, CancellationToken cancellationToken)
    {
        work.Changes++;
        await events.PublishAsync(new SideEffectPublished(integrationEvent.Id), cancellationToken);
        if (log.NextAttempt() == 1)
            return Result.Failure(Error.Failure("Save.Transient", "The save failed; another try may pass."));

        log.ChangesSeenBySuccess = work.Changes;
        return Result.Success();
    }
}

internal sealed class SideEffectHandler(RetryLog log) : IIntegrationEventHandler<SideEffectPublished>
{
    public Task<Result> HandleAsync(SideEffectPublished integrationEvent, CancellationToken cancellationToken)
    {
        log.SideEffects.Enqueue(integrationEvent);
        return Task.FromResult(Result.Success());
    }
}
