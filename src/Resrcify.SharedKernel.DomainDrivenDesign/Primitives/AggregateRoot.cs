using System.Collections.Generic;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

public abstract class AggregateRoot<TId>(TId id)
    : Entity<TId>(id), IAggregateRoot<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> GetDomainEvents() => [.. _domainEvents];

    public void ClearDomainEvents() => _domainEvents.Clear();

    public void RemoveDomainEvent(IDomainEvent domainEvent) => _domainEvents.Remove(domainEvent);

    protected void RaiseDomainEvent(
        IDomainEvent domainEvent) =>
        _domainEvents.Add(
            domainEvent);
}