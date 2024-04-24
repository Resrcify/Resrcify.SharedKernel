using System.Collections.Generic;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.GenericRepository.UnitTests.Models;

namespace Resrcify.SharedKernel.Repository.UnitTests.Models;

public class Person(SocialSecurityNumber id, string name = "Test") : AggregateRoot<SocialSecurityNumber>(id)
{
    public string Name { get; private set; } = name;
    public List<Child> Children = [];
};
