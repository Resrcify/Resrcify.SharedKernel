using System;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

public interface IDeletableEntity
{
    public bool IsDeleted { get; }
    public DateTime DeletedOnUtc { get; }
}