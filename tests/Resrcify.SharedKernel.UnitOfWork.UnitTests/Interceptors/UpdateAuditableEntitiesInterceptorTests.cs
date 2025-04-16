using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Interceptors;

public sealed class UpdateAuditableEntitiesInterceptorTests : DbSetupBase
{
    public UpdateAuditableEntitiesInterceptorTests() : base(new UpdateAuditableEntitiesInterceptor())
    {
    }

    [Fact]
    public async Task SavedChangesAsync_ShouldUpdateCreatedOn_ForAuditableEntities()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        //ASsert
        entity.CreatedOnUtc
            .ShouldBe(now, TimeSpan.FromSeconds(1));

        entity.ModifiedOnUtc
            .ShouldBe(now, TimeSpan.FromSeconds(1));
    }
    [Fact]
    public async Task SaveChangesAsync_ShouldUpdateModifiedOn_ForAuditableEntities()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();
        entity.Name = "Test2";

        // Act
        await DbContext.SaveChangesAsync();

        //ASsert
        entity.ModifiedOnUtc
            .ShouldBe(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SavedChangesAsync_ShouldUpdateCreatedOn_InDatabase()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        //ASsert
        var foundEntity = await DbContext.Persons
            .FirstOrDefaultAsync(x => x.Id == entity.Id);

        foundEntity.ShouldNotBeNull();

        foundEntity!.CreatedOnUtc
            .ShouldBe(now, TimeSpan.FromSeconds(1));

        foundEntity!.ModifiedOnUtc
            .ShouldBe(now, TimeSpan.FromSeconds(1));
    }
    [Fact]
    public async Task SaveChangesAsync_ShouldUpdateModifiedOn_InDatabase()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();
        entity.Name = "Test2";

        // Act
        await DbContext.SaveChangesAsync();


        var foundEntity = await DbContext.Persons
            .FirstOrDefaultAsync(x => x.Id == entity.Id);

        foundEntity.ShouldNotBeNull();

        foundEntity!.ModifiedOnUtc
            .ShouldBe(now, TimeSpan.FromSeconds(1));
    }
}