using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Support;

/// <summary>An integration event whose second handler answers with whatever the test scripted.</summary>
internal sealed record ScriptedEvent(string Id);

/// <summary>What the <see cref="ScriptedEvent"/> handlers did, and what the second one answers next.</summary>
internal sealed class ScriptLog
{
    private int _firstRuns;
    private int _attempts;

    /// <summary>The second handler's answers, one per try; a success once they run out.</summary>
    public ConcurrentQueue<Result> Answers { get; } = new();

    public int FirstRuns => Volatile.Read(ref _firstRuns);

    public int Attempts => Volatile.Read(ref _attempts);

    public ConcurrentQueue<string> Handled { get; } = new();

    public void RecordFirstRun()
        => Interlocked.Increment(ref _firstRuns);

    public Result NextAnswer(string id)
    {
        Interlocked.Increment(ref _attempts);
        if (Answers.TryDequeue(out var answer))
            return answer;
        Handled.Enqueue(id);
        return Result.Success();
    }
}

/// <summary>Always succeeds: shows a retry of the second handler doesn't run it again.</summary>
internal sealed class ScriptedEventFirstHandler(ScriptLog log) : IIntegrationEventHandler<ScriptedEvent>
{
    public Task<Result> HandleAsync(ScriptedEvent integrationEvent, CancellationToken cancellationToken)
    {
        log.RecordFirstRun();
        return Task.FromResult(Result.Success());
    }
}

internal sealed class ScriptedEventSecondHandler(ScriptLog log) : IIntegrationEventHandler<ScriptedEvent>
{
    public Task<Result> HandleAsync(ScriptedEvent integrationEvent, CancellationToken cancellationToken)
        => Task.FromResult(log.NextAnswer(integrationEvent.Id));
}
