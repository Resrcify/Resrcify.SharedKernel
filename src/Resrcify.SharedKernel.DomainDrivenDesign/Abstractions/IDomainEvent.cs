using System;
using MediatR;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

public interface IDomainEvent : INotification
{
    public Guid Id { get; init; }
}
