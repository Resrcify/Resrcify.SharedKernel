using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>
/// The mediator's handler for a request that has an
/// <see cref="IStreamingScatterGatherRequestHandler{TRequest, TItemRequest, TItemResponse, TResponse}"/>: scatter,
/// then handle while the replies arrive. Registered by <c>AddScatterGather</c>.
/// </summary>
internal sealed class StreamingScatterGatherRequestHandler<TRequest, TItemRequest, TItemResponse, TResponse>(
    IStreamingScatterGatherRequestHandler<TRequest, TItemRequest, TItemResponse, TResponse> handler,
    IScatterGatherClient transport)
    : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TItemRequest : class
    where TItemResponse : class
{
    public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken)
    {
        var items = await handler.ScatterAsync(request, cancellationToken);
        var replies = items.Count == 0
            ? AsyncEnumerable.Empty<IScatterReply<TItemResponse>>()
            : transport.StreamAsync<TItemRequest, TItemResponse>(items, handler.Timeout, cancellationToken);
        return await handler.HandleAsync(request, replies, cancellationToken);
    }
}
