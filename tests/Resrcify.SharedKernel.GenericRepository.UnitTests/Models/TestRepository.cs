using Resrcify.SharedKernel.GenericRepository.GenericRepository;
using Resrcify.SharedKernel.GenericRepository.UnitTests.Models;

namespace Resrcify.SharedKernel.GenericRepository.UnitTests.GenericRepository;

public class TestRepository(TestDbContext context) : Repository<TestDbContext, Person, SocialSecurityNumber>(context);
