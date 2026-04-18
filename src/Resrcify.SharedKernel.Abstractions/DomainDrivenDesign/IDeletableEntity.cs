using System;

namespace Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

public interface IDeletableEntity
{
    public bool IsDeleted { get; }
    public DateTime DeletedOnUtc { get; }
}