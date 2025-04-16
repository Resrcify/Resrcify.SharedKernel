using Resrcify.SharedKernel.Repository.Primitives;

namespace Resrcify.SharedKernel.Repository.UnitTests.Models;

internal sealed class TestRepository(TestDbContext context) : Repository<TestDbContext, Person, SocialSecurityNumber>(context);
