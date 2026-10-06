using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Interceptors;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class UpdateDeletableEntitiesInterceptorTests : DbSetupBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock;

    public UpdateDeletableEntitiesInterceptorTests()
        : this(new FakeTimeProvider(Start))
    {
    }

    private UpdateDeletableEntitiesInterceptorTests(FakeTimeProvider clock)
        : base(new UpdateDeletableEntitiesInterceptor(clock))
        => _clock = clock;

    [Fact]
    public async Task SaveChangesAsync_ShouldUpdateDeletableEntities()
    {
        // Arrange
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();
        // Act
        _clock.Advance(TimeSpan.FromMinutes(10));
        DbContext.Persons.Remove(entity);
        await DbContext.SaveChangesAsync();

        //Assert
        entity.DeletedOnUtc
            .ShouldBe(Start.UtcDateTime.AddMinutes(10));   // when it was deleted, not when it was added

        entity.IsDeleted
            .ShouldBeTrue();
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldNotDeleteTheEntity_WhenUpdateDeletableEntities()
    {
        // Arrange
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();

        // Act
        _clock.Advance(TimeSpan.FromMinutes(10));
        DbContext.Persons.Remove(entity);
        await DbContext.SaveChangesAsync();

        //Assert
        var foundEntity = await DbContext.Persons
            .FirstOrDefaultAsync(x => x.Id == entity.Id);

        foundEntity
            .ShouldNotBeNull();

        foundEntity.DeletedOnUtc
            .ShouldBe(Start.UtcDateTime.AddMinutes(10));   // when it was deleted, not when it was added

        foundEntity.IsDeleted
            .ShouldBeTrue();

    }

    [Fact]
    public async Task SaveChangesAsync_ShouldLeaveDeletedOnUtcEmpty_WhenTheEntityIsNotDeleted()
    {
        // Arrange
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");

        // Act
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();

        // Assert
        entity.DeletedOnUtc.ShouldBeNull();
        entity.IsDeleted.ShouldBeFalse();
    }
}
