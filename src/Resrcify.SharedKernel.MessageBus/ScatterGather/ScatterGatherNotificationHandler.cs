using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>
/// The mediator's handler for an event that has an <see cref="IScatterGatherHandler{TEvent, TRequest, TResponse}"/>:
/// scatter, wait for the replies, gather. Registered by <c>AddScatterGather</c>.
/// </summary>
internal sealed class ScatterGatherNotificationHandler<TEvent, TRequest, TResponse>(
    IScatterGatherHandler<TEvent, TRequest, TResponse> handler,
    IScatterGatherClient transport)
    : INotificationHandler<TEvent>
    where TEvent : INotification
    where TRequest : class
    where TResponse : class
{
    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        var requests = await handler.ScatterAsync(notification, cancellationToken);
        IGathered<TResponse> replies = requests.Count == 0
            ? Gathered<TResponse>.Empty
            : await transport.GatherAsync<TRequest, TResponse>(requests, handler.Timeout, cancellationToken);
        await handler.GatherAsync(notification, replies, cancellationToken);
    }
}
