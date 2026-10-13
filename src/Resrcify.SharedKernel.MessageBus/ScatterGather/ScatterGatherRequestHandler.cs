using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>
/// The mediator's handler for a request that has an
/// <see cref="IScatterGatherRequestHandler{TRequest, TItemRequest, TItemResponse, TResponse}"/>: scatter, gather the
/// replies, then handle. Registered by <c>AddScatterGather</c>.
/// </summary>
internal sealed class ScatterGatherRequestHandler<TRequest, TItemRequest, TItemResponse, TResponse>(
    IScatterGatherRequestHandler<TRequest, TItemRequest, TItemResponse, TResponse> handler,
    IScatterGatherClient transport)
    : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TItemRequest : class
    where TItemResponse : class
{
    public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken)
    {
        var items = await handler.ScatterAsync(request, cancellationToken);
        IGathered<TItemResponse> replies = items.Count == 0
            ? Gathered<TItemResponse>.Empty
            : await transport.GatherAsync<TItemRequest, TItemResponse>(items, handler.Timeout, cancellationToken);
        return await handler.HandleAsync(request, replies, cancellationToken);
    }
}
