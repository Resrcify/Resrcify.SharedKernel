using System;
using System.Collections.Generic;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

public class Person : AggregateRoot<SocialSecurityNumber>, IDeletableEntity, IAuditableEntity
{
    public Person(SocialSecurityNumber id, string name = "Test") : base(id)
    {
        Name = name;
    }

    public string Name { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime DeletedOnUtc { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime ModifiedOnUtc { get; set; }
    public List<Child> Children { get; } = [];
}
