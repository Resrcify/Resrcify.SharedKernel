using System;
using System.Threading.Tasks;
using FluentAssertions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Interceptors;

public class UpdateAuditableEntitiesInterceptorTests : DbSetupBase
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

        entity.CreatedOnUtc
            .Should()
            .BeCloseTo(now, TimeSpan.FromMilliseconds(100));

        entity.ModifiedOnUtc
            .Should()
            .BeCloseTo(now, TimeSpan.FromMilliseconds(100));
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

        entity.ModifiedOnUtc
            .Should()
            .BeCloseTo(now, TimeSpan.FromMilliseconds(100));
    }
}