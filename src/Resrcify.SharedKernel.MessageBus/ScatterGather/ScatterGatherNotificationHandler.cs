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
/// scatter, wait for the replies, gather. Registered by <c>AddScatterGather</c>, once per event, request and response
/// types: every handler of those types runs, one after another (they share the outbox's scope and DbContext).
/// </summary>
internal sealed class ScatterGatherNotificationHandler<TEvent, TRequest, TResponse>(
    IEnumerable<IScatterGatherHandler<TEvent, TRequest, TResponse>> handlers,
    IScatterGatherClient transport)
    : INotificationHandler<TEvent>
    where TEvent : INotification
    where TRequest : class
    where TResponse : class
{
    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
            await HandleAsync(handler, notification, cancellationToken);
    }

    private async Task HandleAsync(
        IScatterGatherHandler<TEvent, TRequest, TResponse> handler,
        TEvent notification,
        CancellationToken cancellationToken)
    {
        var requests = await handler.ScatterAsync(notification, cancellationToken);
        IGathered<TResponse> replies = requests.Count == 0
            ? Gathered<TResponse>.Empty
            : await transport.GatherAsync<TRequest, TResponse>(requests, handler.Timeout, cancellationToken);
        await handler.GatherAsync(notification, replies, cancellationToken);
    }
}
