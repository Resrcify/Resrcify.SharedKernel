using System.Collections.Generic;

namespace Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

public interface IAggregateRoot
{
    public IReadOnlyList<IDomainEvent> GetDomainEvents();
    public void ClearDomainEvents();
}

public interface IAggregateRoot<out TId> : IAggregateRoot
    where TId : notnull
{
    TId Id { get; }
}
