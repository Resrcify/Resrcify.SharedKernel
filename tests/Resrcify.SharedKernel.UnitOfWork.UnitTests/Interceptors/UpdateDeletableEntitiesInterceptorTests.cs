using System;
using System.Threading.Tasks;
using FluentAssertions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Xunit;

namespace Resrcify.SharedKernel.GenericUnitOfWork.UnitTests.Interceptors;

public class UpdateDeletableEntitiesInterceptorTests : DbSetupBase
{
    public UpdateDeletableEntitiesInterceptorTests() : base(new UpdateDeletableEntitiesInterceptor())
    {
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldUpdateDeletableEntities()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();
        // Act
        DbContext.Persons.Remove(entity);
        await DbContext.SaveChangesAsync();

        entity.DeletedOnUtc
            .Should()
            .BeCloseTo(now, TimeSpan.FromMilliseconds(100));

        entity.IsDeleted
            .Should()
            .BeTrue();
    }
}