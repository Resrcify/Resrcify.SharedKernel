using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>
/// Like <see cref="IScatterGatherRequestHandler{TRequest, TItemRequest, TItemResponse, TResponse}"/>, but
/// <see cref="HandleAsync"/> gets the replies as they arrive: it can start on the first one, report progress, or
/// stop waiting by stopping the enumeration.
/// </summary>
public interface IStreamingScatterGatherRequestHandler<in TRequest, TItemRequest, TItemResponse, TResponse>
    where TRequest : IRequest<TResponse>
    where TItemRequest : class
    where TItemResponse : class
{
    /// <summary>How long the replies are waited for; the enumeration ends then, or once every item has answered.</summary>
    TimeSpan Timeout { get; }

    /// <summary>The items to ask for, by key.</summary>
    Task<IReadOnlyDictionary<string, TItemRequest>> ScatterAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>Handles the request while the replies arrive, in the order they arrive.</summary>
    Task<TResponse> HandleAsync(TRequest request, IAsyncEnumerable<IScatterReply<TItemResponse>> replies, CancellationToken cancellationToken);
}
