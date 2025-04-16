using System;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

public interface IAuditableEntity
{
    public DateTime CreatedOnUtc { get; }
    public DateTime ModifiedOnUtc { get; }
}