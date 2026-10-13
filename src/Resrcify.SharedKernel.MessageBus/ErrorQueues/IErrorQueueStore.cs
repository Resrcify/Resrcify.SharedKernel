using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.MessageBus.ErrorQueues;

/// <summary>Where the error queue lives: RabbitMQ, or the in-memory network.</summary>
internal interface IErrorQueueStore
{
    /// <summary>How many messages <paramref name="queue"/> holds (0 when it doesn't exist).</summary>
    Task<int> CountAsync(
        string queue,
        CancellationToken cancellationToken);

    /// <summary>
    /// Goes through the first <paramref name="limit"/> messages of <paramref name="queue"/>, oldest first, once each,
    /// doing what <paramref name="decide"/> says with each. The messages kept stay in the queue. Answers what was done
    /// with every message that wasn't kept, and with those that couldn't be moved (kept, with why).
    /// </summary>
    Task<IReadOnlyList<ScanResult>> ScanAsync(
        string queue,
        int limit,
        Func<StoredMessage, ScanStep> decide,
        CancellationToken cancellationToken);
}

/// <summary>A message as stored: its Rebus headers and its body as sent (gzipped, if it was).</summary>
internal sealed record StoredMessage(
    IReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body);

internal enum ScanAction
{
    Keep,
    Delete,
    MoveBack,
}

/// <summary>What to do with a message; <see cref="Destination"/> is where to move it.</summary>
internal readonly record struct ScanStep(
    ScanAction Action,
    string? Destination = null)
{
    public static ScanStep Keep => new(ScanAction.Keep);

    public static ScanStep Delete => new(ScanAction.Delete);

    public static ScanStep MoveTo(string destination)
        => new(ScanAction.MoveBack, destination);
}

/// <summary>What was done with a message; <see cref="Failure"/> says why it couldn't be (it was kept).</summary>
internal sealed record ScanResult(
    StoredMessage Message,
    ScanAction Action,
    string? Failure = null)
{
    public bool Succeeded => Failure is null;
}
