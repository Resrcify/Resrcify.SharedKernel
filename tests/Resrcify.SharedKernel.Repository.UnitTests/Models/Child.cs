using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.Repository.UnitTests.Models;

internal sealed class Child(SocialSecurityNumber id, SocialSecurityNumber personId, string name = "Test") : Entity<SocialSecurityNumber>(id)
{
    public string Name { get; private set; } = name;
    public SocialSecurityNumber PersonId { get; private set; } = personId;

}
