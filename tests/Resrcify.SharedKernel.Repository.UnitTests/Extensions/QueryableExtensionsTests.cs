using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Repository.UnitTests.Models;
using Xunit;
using Resrcify.SharedKernel.Repository.Extensions;
using System.Threading.Tasks;
using FluentAssertions;
using System.Linq;

namespace Resrcify.SharedKernel.Repository.UnitTests.Extensions;

public class ResultExtensionsTests : DbSetupBase
{
    [Fact]
    public async Task WhereIf_ShouldApplyPredicate_WhenConditionIsMet()
    {
        // Arrange
        var entityId1 = SocialSecurityNumber.Create(123);
        var entityId2 = SocialSecurityNumber.Create(456);
        var entity1 = new Person(entityId1);
        var entity2 = new Person(entityId2);
        await DbContext.Persons.AddAsync(entity1);
        await DbContext.Persons.AddAsync(entity2);
        await DbContext.SaveChangesAsync();
        // Act
        var result = await DbContext.Persons
            .AsNoTracking()
            .WhereIf(entityId1.Value > 100, p => (int)p.Id > 200)
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
        await DbContext.Persons.AddAsync(entity1);
        await DbContext.Persons.AddAsync(entity2);
        await DbContext.SaveChangesAsync();
        // Act
        var result = await DbContext.Persons
            .AsNoTracking()
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

    [Fact]
    public async Task IncludeIf_ShouldApplyPredicate_WhenConditionIsMet()
    {
        // Arrange
        var entityId1 = SocialSecurityNumber.Create(123);
        var entityId2 = SocialSecurityNumber.Create(456);
        var entity1 = new Person(entityId1);
        var entity2 = new Child(entityId2, entityId1);
        entity1.Children.Add(entity2);

        await DbContext.Persons.AddAsync(entity1);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await DbContext.Persons
            .IncludeIf(true, p => p.Children)
            .AsNoTracking()
            .ToListAsync();

        // Assert
        result
            .Should()
            .NotBeNull();

        result.First().Children
            .Should()
            .Contain(entity2);

        result.First().Children
            .Should()
            .HaveCount(1);
    }
    [Fact]
    public async Task IncludeIf_ShouldNotApplyPredicate_WhenConditionIsNotMet()
    {
        // Arrange
        var entityId1 = SocialSecurityNumber.Create(123);
        var entityId2 = SocialSecurityNumber.Create(456);
        var entity1 = new Person(entityId1);
        var entity2 = new Child(entityId2, entityId1);
        entity1.Children.Add(entity2);

        await DbContext.Persons.AddAsync(entity1);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await DbContext.Persons
            .IncludeIf(false, p => p.Children)
            .AsNoTracking()
            .ToListAsync();

        // Assert
        result
            .Should()
            .NotBeNull();

        result.First().Children
           .Should()
           .HaveCount(0);
    }
}