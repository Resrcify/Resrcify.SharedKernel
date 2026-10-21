using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>
/// One chunk of a chunked gather (<see cref="IScatterGatherClient.GatherInChunksAsync"/>): which items it holds, and
/// what came of it (<see cref="Status"/>): what came back (<see cref="Gathered"/>), what its gather threw
/// (<see cref="Exception"/>), or nothing, when the budget left no room to ask it (<see cref="GatheredChunkStatus.NotAsked"/>,
/// reported once for every chunk not asked).
/// </summary>
/// <typeparam name="TResponse">The responder's answer to one item.</typeparam>
public sealed class GatheredChunk<TResponse>
    where TResponse : class
{
    private GatheredChunk(
        int index,
        int count,
        IReadOnlyList<string> keys,
        TimeSpan timeout,
        GatheredChunkStatus status,
        IGathered<TResponse>? gathered,
        Exception? exception)
    {
        Index = index;
        Count = count;
        Keys = keys;
        Timeout = timeout;
        Status = status;
        Gathered = gathered;
        Exception = exception;
        Counts = gathered is null
            ? GatheredCounts.None
            : GatheredCounts.Of(gathered);
    }

    /// <summary>
    /// The chunk's position, from 0, in the order the requests were given; for the chunks not asked, the first of them.
    /// </summary>
    public int Index { get; }

    /// <summary>How many chunks the requests make in all, asked or not.</summary>
    public int Count { get; }

    /// <summary>The items of this chunk; for the chunks not asked, the items of all of them.</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>The chunk's timeout: what it was asked with, or, not asked, what no longer fit in the budget.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>What came of the chunk.</summary>
    public GatheredChunkStatus Status { get; }

    /// <summary>What came back, when the chunk was gathered; otherwise <see langword="null"/>.</summary>
    public IGathered<TResponse>? Gathered { get; }

    /// <summary>What the chunk's gather threw, when it failed; otherwise <see langword="null"/>.</summary>
    public Exception? Exception { get; }

    /// <summary>
    /// The chunk's counts (<see cref="GatheredCounts.Of"/>), to sum with <c>+</c>; <see cref="GatheredCounts.None"/> for
    /// a chunk that failed or wasn't asked, whose items <see cref="Keys"/> gives.
    /// </summary>
    public GatheredCounts Counts { get; }

    /// <summary>Whether the chunk was gathered (<see cref="Gathered"/> is set).</summary>
    [MemberNotNullWhen(true, nameof(Gathered))]
    public bool IsGathered
        => Status == GatheredChunkStatus.Gathered;

    /// <summary>Whether the chunk's gather threw (<see cref="Exception"/> is set).</summary>
    [MemberNotNullWhen(true, nameof(Exception))]
    public bool IsFailed
        => Status == GatheredChunkStatus.Failed;

    /// <summary>Whether the chunks from this one on weren't asked: the budget had no room left.</summary>
    public bool IsNotAsked
        => Status == GatheredChunkStatus.NotAsked;

    internal static GatheredChunk<TResponse> Of(
        int index,
        int count,
        IReadOnlyList<string> keys,
        TimeSpan timeout,
        IGathered<TResponse> gathered)
        => new(index, count, keys, timeout, GatheredChunkStatus.Gathered, gathered, exception: null);

    internal static GatheredChunk<TResponse> FailedWith(
        int index,
        int count,
        IReadOnlyList<string> keys,
        TimeSpan timeout,
        Exception exception)
        => new(index, count, keys, timeout, GatheredChunkStatus.Failed, gathered: null, exception);

    internal static GatheredChunk<TResponse> NotAskedFrom(
        int index,
        int count,
        IReadOnlyList<string> keys,
        TimeSpan timeout)
        => new(index, count, keys, timeout, GatheredChunkStatus.NotAsked, gathered: null, exception: null);
}
