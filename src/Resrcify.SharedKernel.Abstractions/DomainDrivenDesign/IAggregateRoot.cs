using System.Collections.Generic;

namespace Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

public interface IAggregateRoot
{
    public IReadOnlyList<IDomainEvent> GetDomainEvents();
    public void ClearDomainEvents();

    /// <summary>
    /// Takes back one event raised but not yet saved (e.g. the unit of work undoing a failed command's work); one not
    /// raised by this aggregate is ignored.
    /// </summary>
    public void RemoveDomainEvent(IDomainEvent domainEvent);
}

public interface IAggregateRoot<out TId> : IAggregateRoot
    where TId : notnull
{
    TId Id { get; }
}
