namespace Resrcify.SharedKernel.Abstractions.Messaging;

public interface IDomainEventHandler<in TEvent>
    : INotificationHandler<TEvent>
    where TEvent : INotification;
