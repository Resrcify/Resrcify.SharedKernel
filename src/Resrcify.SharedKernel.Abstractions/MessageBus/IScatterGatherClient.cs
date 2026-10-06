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
}
