using System;
using MediatR;

namespace Resrcify.SharedKernel.Abstractions;

public interface IDomainEvent : INotification
{
    public Guid Id { get; init; }
}
