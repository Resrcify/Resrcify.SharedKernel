using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Interceptors;

public sealed class UpdateDeletableEntitiesInterceptorTests : DbSetupBase
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
            .ShouldBe(now, TimeSpan.FromSeconds(1));

        entity.IsDeleted
            .ShouldBeTrue();
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
            .ShouldNotBeNull();

        foundEntity!.DeletedOnUtc
            .ShouldBe(now, TimeSpan.FromSeconds(1));

        foundEntity!.IsDeleted
            .ShouldBeTrue();

    }
}