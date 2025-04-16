using System.Collections.Generic;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

public interface IAggregateRoot
{
    IReadOnlyList<IDomainEvent> GetDomainEvents();
    void ClearDomainEvents();
}
