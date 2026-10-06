using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>
/// Handles a mediator request with answers from another service: <see cref="ScatterAsync"/> says what to ask
/// (one item per key), the message bus asks and gathers the replies, and <see cref="HandleAsync"/> runs with
/// them. Sent like any request (<c>ISender.Send</c>), so pipeline behaviors still run first.
/// </summary>
/// <remarks>
/// Found by the message bus' <c>AddScatterGather</c> in the assemblies the mediator scans. The caller waits up to
/// <see cref="Timeout"/>; nothing survives a crash. For work that must, raise a domain event and handle it with
/// an <see cref="IScatterGatherHandler{TEvent, TRequest, TResponse}"/> (through the outbox) instead.
/// </remarks>
public interface IScatterGatherRequestHandler<in TRequest, TItemRequest, TItemResponse, TResponse>
    where TRequest : IRequest<TResponse>
    where TItemRequest : class
    where TItemResponse : class
{
    /// <summary>How long to wait for replies; items that haven't answered by then are unanswered.</summary>
    TimeSpan Timeout { get; }

    /// <summary>The items to ask for, by key.</summary>
    Task<IReadOnlyDictionary<string, TItemRequest>> ScatterAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>Handles the request with every reply that arrived (all of them, or what came before the timeout).</summary>
    Task<TResponse> HandleAsync(TRequest request, IGathered<TItemResponse> replies, CancellationToken cancellationToken);
}
