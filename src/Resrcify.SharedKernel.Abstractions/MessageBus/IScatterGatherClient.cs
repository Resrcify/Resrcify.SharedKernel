using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>
/// Asks another service directly, for code that isn't a scatter-gather handler: one request, a batch, or a
/// stream of replies. Registered by the message bus' <c>AddScatterGather</c>.
/// </summary>
/// <remarks>
/// The caller waits up to the timeout and nothing survives a crash. For work that must, raise a domain event and
/// handle it with an <see cref="IScatterGatherHandler{TEvent, TRequest, TResponse}"/> (through the outbox).
/// </remarks>
public interface IScatterGatherClient
{
    /// <summary>
    /// One request: its response, the responder's errors, or <c>ScatterGather.Unanswered</c>
    /// (<c>ErrorType.Timeout</c>) if no answer came within <paramref name="timeout"/>.
    /// </summary>
    Task<Result<TResponse>> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;

    /// <summary>Asks for every item, and returns what came back once all have answered or the timeout passed.</summary>
    Task<IGathered<TResponse>> GatherAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;

    /// <summary>Asks for every item, and yields the replies as they arrive, until all have answered or the timeout passed.</summary>
    IAsyncEnumerable<IScatterReply<TResponse>> StreamAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;

    /// <summary>
    /// Asks for the items <paramref name="chunkSize"/> at a time, one chunk after another, each with its own timeout
    /// (<paramref name="timeoutForChunk"/>, given the chunk's number of items), and yields each chunk once it is
    /// gathered (<see cref="GatheredChunk{TResponse}"/>). Lazily: a chunk is asked only when the caller asks for it, so a
    /// caller that processes each chunk before taking the next holds one chunk's answers at a time.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The chunks follow the order of <paramref name="requests"/>' keys.</item>
    /// <item>A chunk whose gather throws is yielded as failed (<see cref="GatheredChunk{TResponse}.Exception"/>), and the
    /// next one is still asked.</item>
    /// <item>With a <paramref name="budget"/> (counted from the first chunk, the caller's work between chunks included),
    /// a chunk whose timeout no longer fits in what is left of it isn't asked, nor any after it: they are yielded once,
    /// together, as not asked, with their keys, and the gather ends.</item>
    /// <item>A cancellation of <paramref name="cancellationToken"/> is the caller's: it throws, rather than failing a
    /// chunk.</item>
    /// <item>No requests, no chunks. A <paramref name="chunkSize"/> below 1 or a negative budget throws when called.</item>
    /// </list>
    /// The message bus' client measures the budget on the container's <see cref="TimeProvider"/>; this default (for a
    /// stand-in that implements only <see cref="GatherAsync{TRequest, TResponse}"/>) on the system clock.
    /// </remarks>
    IAsyncEnumerable<GatheredChunk<TResponse>> GatherInChunksAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        int chunkSize,
        Func<int, TimeSpan> timeoutForChunk,
        TimeSpan? budget = null,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
        => ChunkedGathering.GatherAsync<TRequest, TResponse>(
            this,
            requests,
            chunkSize,
            timeoutForChunk,
            budget,
            TimeProvider.System,
            cancellationToken);
}
