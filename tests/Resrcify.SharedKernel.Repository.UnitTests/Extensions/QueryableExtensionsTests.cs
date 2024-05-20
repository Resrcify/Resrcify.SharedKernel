using System;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.GenericRepository.UnitTests.GenericRepository;
using Resrcify.SharedKernel.GenericRepository.UnitTests.Models;
using Resrcify.SharedKernel.Repository.UnitTests.Models;
using Xunit;
using Resrcify.SharedKernel.Repository.Extensions;
using System.Threading.Tasks;
using FluentAssertions;

namespace Resrcify.SharedKernel.Repository.UnitTests.Extensions;

public class ResultExtensionsTests
{
    private static TestDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public async Task WhereIf_ShouldApplyPredicate_WhenConditionIsMet()
    {
        // Arrange
        var entityId1 = SocialSecurityNumber.Create(123);
        var entityId2 = SocialSecurityNumber.Create(456);
        var entity1 = new Person(entityId1);
        var entity2 = new Person(entityId2);
        using var dbContext = CreateDbContext();
        dbContext.Add(entity1);
        dbContext.Add(entity2);
        await dbContext.SaveChangesAsync();
        // Act
        var result = await dbContext.Persons
            .WhereIf(entityId1.Value > 100, p => p.Id.Value > 200)
            .ToListAsync();

        // Assert
        result
            .Should()
            .NotBeNull();

        result
            .Should()
            .Contain(entity2);

        result
            .Should()
            .HaveCount(1);
    }

    [Fact]
    public async Task WhereIf_ShouldNotApplyPredicate_WhenConditionIsNotMet()
    {
        // Arrange
        var entityId1 = SocialSecurityNumber.Create(123);
        var entityId2 = SocialSecurityNumber.Create(456);
        var entity1 = new Person(entityId1);
        var entity2 = new Person(entityId2);
        using var dbContext = CreateDbContext();
        dbContext.Add(entity1);
        dbContext.Add(entity2);
        await dbContext.SaveChangesAsync();
        // Act
        var result = await dbContext.Persons
            .WhereIf(entityId1.Value > 500, p => p.Id.Value > 200)
            .ToListAsync();

        // Assert
        result
            .Should()
            .NotBeNull();

        result
            .Should()
            .Contain(entity1);

        result
            .Should()
            .Contain(entity2);

        result
            .Should()
            .HaveCount(2);
    }
}