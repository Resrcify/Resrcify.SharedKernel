using Resrcify.SharedKernel.GenericRepository.UnitTests.GenericRepository;
using Resrcify.SharedKernel.GenericRepository.UnitTests.Models;
using Resrcify.SharedKernel.Repository.Primitives;

namespace Resrcify.SharedKernel.Repository.UnitTests.Models;

public class TestRepository(TestDbContext context) : Repository<TestDbContext, Person, SocialSecurityNumber>(context);
