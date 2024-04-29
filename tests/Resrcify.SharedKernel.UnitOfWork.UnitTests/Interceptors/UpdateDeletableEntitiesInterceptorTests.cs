using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

        //Assert
        entity.DeletedOnUtc
            .Should()
            .BeCloseTo(now, TimeSpan.FromSeconds(1));

        entity.IsDeleted
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldNotDeleteTheEntity_WhenUpdateDeletableEntities()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();

        // Act
        DbContext.Persons.Remove(entity);
        await DbContext.SaveChangesAsync();

        //Assert
        var foundEntity = await DbContext.Persons
            .FirstOrDefaultAsync(x => x.Id == entity.Id);

        foundEntity
            .Should()
            .NotBeNull();

        foundEntity!.DeletedOnUtc
            .Should()
            .BeCloseTo(now, TimeSpan.FromSeconds(1));

        foundEntity!.IsDeleted
            .Should()
            .BeTrue();

    }
}