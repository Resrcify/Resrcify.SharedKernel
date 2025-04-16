using System.Collections.Generic;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

public interface IAggregateRoot
{
    public IReadOnlyList<IDomainEvent> GetDomainEvents();
    public void ClearDomainEvents();
}
