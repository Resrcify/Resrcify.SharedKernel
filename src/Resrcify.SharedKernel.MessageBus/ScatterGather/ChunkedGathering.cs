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
    /// Checks the arguments now and returns the chunks, gathered one at a time and one ahead of the caller: the first
    /// when the caller asks for it, each next one as soon as the one before is gathered; see
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
        // Cancelled when the caller stops (or cancels): the chunk being gathered ahead stops with it.
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<GatheredChunk<TResponse>>? next = null;
        try
        {
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = await (next ?? StartChunk(index));

                // One chunk ahead: the next is asked as soon as this one is gathered, so it is gathered while the
                // caller handles this one; the one after it waits for the caller to take the next. One gather at a
                // time, and two chunks' answers at most: the caller's and the one being gathered.
                next = chunk.IsNotAsked || index + 1 == count
                    ? null
                    : StartChunk(index + 1);
                yield return chunk;
                if (chunk.IsNotAsked)
                    yield break;
            }
        }
        finally
        {
            // The caller stopped early (or the gather threw): the chunk gathered ahead is stopped, and has ended
            // before the caller's DisposeAsync returns. Its answers are dropped.
            await StopAsync(stopping, next);
        }

        // Starts chunk index's gather, or gives it as not asked when its timeout no longer fits in the budget (and
        // every chunk after it with it): the budget is checked as the chunk is about to start.
        Task<GatheredChunk<TResponse>> StartChunk(int index)
        {
            var first = index * chunkSize;
            var chunkKeys = keys.GetRange(first, Math.Min(chunkSize, keys.Count - first));
            var timeout = timeoutForChunk(chunkKeys.Count);
            if (budget is { } total && time.GetElapsedTime(started) + timeout > total)
                return Task.FromResult(GatheredChunk<TResponse>.NotAsked(
                    index,
                    count,
                    keys.GetRange(first, keys.Count - first),
                    timeout));

            // The caller cancelled while handling the chunk before: nothing more is asked.
            if (stopping.IsCancellationRequested)
                return Task.FromCanceled<GatheredChunk<TResponse>>(stopping.Token);

            return GatherChunkAsync<TRequest, TResponse>(
                client,
                requests,
                chunkKeys,
                index,
                count,
                timeout,
                stopping.Token);
        }
    }

    // Cancels the chunk gathered ahead, if one is under way, and waits for it to end. What it gives or throws is
    // dropped (and observed, so it is never reported unobserved).
    private static async Task StopAsync(
        CancellationTokenSource stopping,
        Task? ahead)
    {
        if (ahead is null)
            return;

        if (!ahead.IsCompleted)
            await stopping.CancelAsync();

        await ahead.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _ = ahead.Exception;
    }

    // One chunk: what came back, or what its gather threw. The caller's cancellation (or its stopping early, which
    // cancels the token too) isn't the chunk's failure: it ends the gather.
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
