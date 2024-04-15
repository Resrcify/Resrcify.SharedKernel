using System;

namespace Resrcify.SharedKernel.Abstractions;

public interface IAuditableEntity
{
    DateTime CreatedOnUtc { get; set; }
    DateTime ModifiedOnUtc { get; set; }
}