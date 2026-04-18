using System;

namespace Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

public interface IAuditableEntity
{
    public DateTime CreatedOnUtc { get; }
    public DateTime ModifiedOnUtc { get; }
}