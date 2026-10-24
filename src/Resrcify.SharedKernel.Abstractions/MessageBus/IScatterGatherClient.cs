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
    /// gathered (<see cref="GatheredChunk{TResponse}"/>). One chunk ahead of the caller: the first chunk is asked when the
    /// caller asks for it, and each next one as soon as the one before is gathered, so it is gathered while the caller
    /// handles that one; the one after it waits until the caller takes it. One gather is under way at a time, and at
    /// most two chunks' answers are held: the one the caller holds and the one being gathered.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The chunks follow the order of <paramref name="requests"/>' keys, each yielded once.</item>
    /// <item>A chunk whose gather throws is yielded as failed (<see cref="GatheredChunk{TResponse}.Exception"/>), and the
    /// next one is still asked.</item>
    /// <item>With a <paramref name="budget"/> (counted from the first chunk, the caller's work between chunks included),
    /// a chunk whose timeout no longer fits in what is left of it isn't asked, nor any after it: they are yielded once,
    /// together, as not asked, with their keys, and the gather ends. The check is made as a chunk is about to start,
    /// which is ahead of the caller's work on the chunk before: that work counts against the chunks after it.</item>
    /// <item>A cancellation of <paramref name="cancellationToken"/> is the caller's: it throws, rather than failing a
    /// chunk.</item>
    /// <item>Stopping early (a <c>break</c>, an exception, <c>DisposeAsync</c> of the enumerator) or cancelling cancels
    /// the chunk being gathered ahead, and the enumerator's <c>DisposeAsync</c> returns once it has ended; its answers
    /// are dropped. Nothing is asked after that.</item>
    /// <item>No requests, no chunks. A <paramref name="chunkSize"/> below 1 or a negative budget throws when called.</item>
    /// </list>
    /// The message bus' client measures the budget on the container's <see cref="TimeProvider"/>; the test double
    /// (<c>ScatterGatherClientStandIn</c>, MessageBus.Testing) on the test's.
    /// </remarks>
    IAsyncEnumerable<GatheredChunk<TResponse>> GatherInChunksAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        int chunkSize,
        Func<int, TimeSpan> timeoutForChunk,
        TimeSpan? budget = null,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;
}
