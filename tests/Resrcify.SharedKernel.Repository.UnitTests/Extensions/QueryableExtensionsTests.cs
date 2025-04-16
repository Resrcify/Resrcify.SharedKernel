using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Repository.UnitTests.Models;
using Xunit;
using Resrcify.SharedKernel.Repository.Extensions;
using System.Threading.Tasks;
using System.Linq;
using Shouldly;

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
            .ShouldNotBeNull();

        result
            .ShouldContain(entity2);

        result.ShouldHaveSingleItem();
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
            .ShouldNotBeNull();

        result
            .ShouldContain(entity1);

        result
            .ShouldContain(entity2);

        result.Count
            .ShouldBe(2);
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
            .ShouldNotBeNull();

        result[0].Children
            .ShouldContain(entity2);

        result[0].Children.ShouldHaveSingleItem();
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
            .ShouldNotBeNull();

        result[0].Children
           .ShouldBeEmpty();
    }
}