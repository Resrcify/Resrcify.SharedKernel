using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.GenericRepository.UnitTests.Models;

namespace Resrcify.SharedKernel.Repository.UnitTests.Models;

public class Child(SocialSecurityNumber id, string name = "Test") : Entity<SocialSecurityNumber>(id)
{
    public string Name { get; private set; } = name;
}
