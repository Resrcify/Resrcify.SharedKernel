using System.Collections.Generic;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

public interface IAggregateRoot
{
    IReadOnlyCollection<IDomainEvent> GetDomainEvents();
    void ClearDomainEvents();
}
