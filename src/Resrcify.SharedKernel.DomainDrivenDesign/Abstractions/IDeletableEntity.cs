using System;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

public interface IDeletableEntity
{
    bool IsDeleted { get; }
    DateTime DeletedOnUtc { get; }
}