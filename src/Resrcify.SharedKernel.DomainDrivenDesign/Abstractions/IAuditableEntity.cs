using System;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

public interface IAuditableEntity
{
    DateTime CreatedOnUtc { get; }
    DateTime ModifiedOnUtc { get; }
}