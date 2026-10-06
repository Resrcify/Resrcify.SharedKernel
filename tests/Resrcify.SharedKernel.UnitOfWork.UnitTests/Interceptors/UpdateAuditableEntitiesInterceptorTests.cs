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
public sealed class UpdateAuditableEntitiesInterceptorTests : DbSetupBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock;

    public UpdateAuditableEntitiesInterceptorTests()
        : this(new FakeTimeProvider(Start))
    {
    }

    private UpdateAuditableEntitiesInterceptorTests(FakeTimeProvider clock)
        : base(new UpdateAuditableEntitiesInterceptor(clock))
        => _clock = clock;

    [Fact]
    public async Task SavedChangesAsync_ShouldSetCreatedAndModifiedOnFromTheClock_WhenTheEntityIsAdded()
    {
        // Arrange
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert
        entity.CreatedOnUtc.ShouldBe(Start.UtcDateTime);
        entity.ModifiedOnUtc.ShouldBe(Start.UtcDateTime);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldMoveOnlyModifiedOn_WhenTheEntityIsChangedLater()
    {
        // Arrange
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();
        _clock.Advance(TimeSpan.FromHours(1));
        entity.Name = "Test2";

        // Act
        await DbContext.SaveChangesAsync();

        // Assert
        entity.CreatedOnUtc.ShouldBe(Start.UtcDateTime);
        entity.ModifiedOnUtc.ShouldBe(Start.UtcDateTime.AddHours(1));
    }

    [Fact]
    public async Task SavedChangesAsync_ShouldStoreCreatedOn_InDatabase()
    {
        // Arrange
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert
        var foundEntity = await DbContext.Persons
            .FirstOrDefaultAsync(x => x.Id == entity.Id);
        foundEntity.ShouldNotBeNull();
        foundEntity.CreatedOnUtc.ShouldBe(Start.UtcDateTime);
        foundEntity.ModifiedOnUtc.ShouldBe(Start.UtcDateTime);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldStoreModifiedOn_InDatabase()
    {
        // Arrange
        var entity = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();
        _clock.Advance(TimeSpan.FromMinutes(30));
        entity.Name = "Test2";

        // Act
        await DbContext.SaveChangesAsync();

        // Assert
        var foundEntity = await DbContext.Persons
            .FirstOrDefaultAsync(x => x.Id == entity.Id);
        foundEntity.ShouldNotBeNull();
        foundEntity.ModifiedOnUtc.ShouldBe(Start.UtcDateTime.AddMinutes(30));
    }
}
