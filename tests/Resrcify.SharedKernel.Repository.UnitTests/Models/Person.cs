using System.Collections.Generic;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.Repository.UnitTests.Models;

internal sealed class Person(
    SocialSecurityNumber id,
    string name = "Test")
    : AggregateRoot<SocialSecurityNumber>(id)
{
    public string Name { get; private set; } = name;
    public List<Child> Children = [];
};
