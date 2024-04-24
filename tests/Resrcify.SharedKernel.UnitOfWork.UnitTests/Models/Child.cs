using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

public class Child(SocialSecurityNumber id, string name = "Test") : Entity<SocialSecurityNumber>(id)
{
    public string Name { get; private set; } = name;
}
