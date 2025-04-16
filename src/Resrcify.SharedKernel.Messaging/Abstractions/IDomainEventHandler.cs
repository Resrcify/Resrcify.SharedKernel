using MediatR;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface IDomainEventHandler<in TEvent>
    : INotificationHandler<TEvent>
    where TEvent : IDomainEvent;
