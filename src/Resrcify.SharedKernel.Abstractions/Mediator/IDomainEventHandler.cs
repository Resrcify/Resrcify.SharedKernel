namespace Resrcify.SharedKernel.Abstractions.Mediator;

public interface IDomainEventHandler<in TEvent>
    : INotificationHandler<TEvent>
    where TEvent : INotification;
