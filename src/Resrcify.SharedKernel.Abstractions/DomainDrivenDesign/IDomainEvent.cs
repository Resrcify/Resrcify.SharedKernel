using System;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

public interface IDomainEvent
    : INotification
{
    public Guid Id { get; init; }
}
