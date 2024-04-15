using System.Collections.Generic;

namespace Resrcify.SharedKernel.Abstractions;

public interface IAggregateRoot
{
    IReadOnlyCollection<IDomainEvent> GetDomainEvents();
    void ClearDomainEvents();
}
