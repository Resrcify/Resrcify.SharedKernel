using System;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>Handles an integration event by publishing a mediator notification made from it (<c>ForwardEvent</c>).</summary>
internal sealed class NotificationForwarder<TEvent>(
    IPublisher publisher,
    Func<TEvent, INotification> toNotification)
    : IIntegrationEventHandler<TEvent>
    where TEvent : class
{
    public async Task<Result> HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken)
    {
        await publisher.Publish(toNotification(integrationEvent), cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Handles an integration event by sending a mediator command made from it (<c>ForwardEvent</c>); the command's
/// <see cref="Result"/> is the handler's, so its failures are rejected or retried like any handler's.
/// </summary>
internal sealed class CommandForwarder<TEvent>(
    ISender sender,
    Func<TEvent, IRequest<Result>> toCommand)
    : IIntegrationEventHandler<TEvent>
    where TEvent : class
{
    // Sent by its runtime type, so a command returning a value (an IRequest<Result<T>>) finds its handler too.
    public async Task<Result> HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken)
    {
        object command = toCommand(integrationEvent);
        return await sender.Send(command, cancellationToken) as Result
            ?? throw new InvalidOperationException($"{command.GetType().Name}'s handler returned no result.");
    }
}
