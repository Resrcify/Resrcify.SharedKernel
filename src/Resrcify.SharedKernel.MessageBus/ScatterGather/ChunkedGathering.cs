using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Resrcify.SharedKernel.Abstractions.MessageBus;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>
/// <see cref="IScatterGatherClient.GatherInChunksAsync"/> on a client's own <see cref="IScatterGatherClient.GatherAsync"/>,
/// in one place: the message bus' client runs it on the container's clock, its test double
/// (<c>ScatterGatherClientStandIn</c>, MessageBus.Testing) on the test's.
/// </summary>
internal static class ChunkedGathering
{
    /// <summary>
    /// Checks the arguments now and returns the chunks, gathered one at a time as they are enumerated; see
    /// <see cref="IScatterGatherClient.GatherInChunksAsync"/>. <paramref name="time"/> measures the budget.
    /// </summary>
    public static IAsyncEnumerable<GatheredChunk<TResponse>> GatherAsync<TRequest, TResponse>(
        IScatterGatherClient client,
        IReadOnlyDictionary<string, TRequest> requests,
        int chunkSize,
        Func<int, TimeSpan> timeoutForChunk,
        TimeSpan? budget,
        TimeProvider time,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(timeoutForChunk);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        if (budget < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "A budget can't be negative.");

        return GatherChunksAsync<TRequest, TResponse>(
            client,
            requests,
            chunkSize,
            timeoutForChunk,
            budget,
            time,
            cancellationToken);
    }

    private static async IAsyncEnumerable<GatheredChunk<TResponse>> GatherChunksAsync<TRequest, TResponse>(
        IScatterGatherClient client,
        IReadOnlyDictionary<string, TRequest> requests,
        int chunkSize,
        Func<int, TimeSpan> timeoutForChunk,
        TimeSpan? budget,
        TimeProvider time,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        var keys = requests.Keys.ToList();
        var count = (keys.Count + chunkSize - 1) / chunkSize;
        // The budget counts from the first chunk asked, and takes in the caller's work between chunks.
        var started = time.GetTimestamp();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = index * chunkSize;
            var chunkKeys = keys.GetRange(first, Math.Min(chunkSize, keys.Count - first));
            var timeout = timeoutForChunk(chunkKeys.Count);
            if (budget is { } total && time.GetElapsedTime(started) + timeout > total)
            {
                yield return GatheredChunk<TResponse>.NotAsked(
                    index,
                    count,
                    keys.GetRange(first, keys.Count - first),
                    timeout);
                yield break;
            }

            yield return await GatherChunkAsync<TRequest, TResponse>(
                client,
                requests,
                chunkKeys,
                index,
                count,
                timeout,
                cancellationToken);
        }
    }

    // One chunk: what came back, or what its gather threw. The caller's cancellation isn't the chunk's failure: it ends
    // the gather.
    private static async Task<GatheredChunk<TResponse>> GatherChunkAsync<TRequest, TResponse>(
        IScatterGatherClient client,
        IReadOnlyDictionary<string, TRequest> requests,
        List<string> keys,
        int index,
        int count,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        var chunk = new Dictionary<string, TRequest>(keys.Count, StringComparer.Ordinal);
        foreach (var key in keys)
            chunk[key] = requests[key];

        try
        {
            var gathered = await client.GatherAsync<TRequest, TResponse>(chunk, timeout, cancellationToken);
            return GatheredChunk<TResponse>.FromGathered(index, count, keys, timeout, gathered);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return GatheredChunk<TResponse>.FromFailure(index, count, keys, timeout, exception);
        }
    }
}
