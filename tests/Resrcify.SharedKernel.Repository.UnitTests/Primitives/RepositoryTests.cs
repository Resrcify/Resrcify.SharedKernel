using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Repository.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Repository.UnitTests.Primitives;

public class RepositoryTests
{
    private static TestDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsEntity_WhenEntityExists()
    {
        // Arrange
        var entityId = SocialSecurityNumber.Create(123);
        var entity = new Person(entityId);
        using var dbContext = CreateDbContext();
        dbContext.Add(entity);
        await dbContext.SaveChangesAsync();

        var repository = new TestRepository(dbContext);
        // Act
        var result = await repository.GetByIdAsync(entityId);

        // Assert
        result
            .ShouldNotBeNull();

        result
            .ShouldBeEquivalentTo(entity);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenEntityDoesntExists()
    {
        // Arrange
        var entityId = SocialSecurityNumber.Create(123);
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        // Act
        var result = await repository.GetByIdAsync(entityId);

        // Assert
        result
            .ShouldBeNull();
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ReturnsCorrectEntity_WhenPredicateMatches()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entityId = SocialSecurityNumber.Create(123);
        var entity = new Person(entityId);
        dbContext.Persons.Add(entity);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await repository.FirstOrDefaultAsync(x => x.Id == entityId);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(entity);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ReturnsNull_WhenNoEntityMatchesPredicate()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entityId = SocialSecurityNumber.Create(123);

        // Act
        var result = await repository.FirstOrDefaultAsync(x => x.Id == entityId);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ReturnsFirstEntity_WhenMultipleEntitiesMatch()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entityId = SocialSecurityNumber.Create(1);
        var entityId2 = SocialSecurityNumber.Create(2);
        var uniqueName = "String";
        var firstEntity = new Person(entityId, uniqueName);
        var secondEntity = new Person(entityId2, uniqueName); // Same ID, for example purposes

        dbContext.Persons.AddRange(firstEntity, secondEntity);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await repository.FirstOrDefaultAsync(x => x.Name == uniqueName);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(firstEntity);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithSpecification_ReturnsCorrectEntity_WhenCriteriaMatches()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entityId = SocialSecurityNumber.Create(123);
        var entity = new Person(entityId, "SpecificName");
        dbContext.Persons.Add(entity);
        await dbContext.SaveChangesAsync();

        var spec = new PersonSpecification(x => x.Name == "SpecificName");

        // Act
        var result = await repository.FirstOrDefaultAsync(spec);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(entity);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithSpecification_ReturnsNull_WhenNoEntityMatchesCriteria()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var spec = new PersonSpecification(x => x.Name == "NonExistentName");

        // Act
        var result = await repository.FirstOrDefaultAsync(spec);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllEntitiesAsAsyncEnumerable()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        var entities = new List<Person>
        {
            new(SocialSecurityNumber.Create(123), "Alice"),
            new(SocialSecurityNumber.Create(456), "Bob")
        };

        dbContext.Persons.AddRange(entities);
        await dbContext.SaveChangesAsync();

        // Act
        var results = repository.GetAllAsync();

        // Assert
        var resultList = await results.ToListAsync();
        resultList.Count.ShouldBe(entities.Count);
        resultList.ShouldBe(entities);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsNoEntitiesAsAsyncEnumerable_WhenDatabaseIsEmpty()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        // Act
        var results = repository.GetAllAsync();

        // Assert
        var resultList = await results.ToListAsync();
        resultList.ShouldBeEmpty();
    }


    [Fact]
    public async Task FindAsync_ReturnsFilteredEntitiesAsAsyncEnumerable_WhenPredicateMatches()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        var matchingEntity = new Person(SocialSecurityNumber.Create(123), "Alice");
        var nonMatchingEntity = new Person(SocialSecurityNumber.Create(456), "Bob");

        dbContext.Persons.AddRange(matchingEntity, nonMatchingEntity);
        await dbContext.SaveChangesAsync();

        // Act
        var results = repository.FindAsync(p => p.Name == "Alice");

        // Assert
        var resultList = await results.ToListAsync();
        resultList.ShouldHaveSingleItem();
        resultList.ShouldContain(matchingEntity);
    }
    [Fact]
    public async Task FindAsync_ReturnsNoEntitiesAsAsyncEnumberable_WhenNoDataMatchesPredicate()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        var entities = new List<Person>
        {
            new(SocialSecurityNumber.Create(123), "Alice"),
            new(SocialSecurityNumber.Create(456), "Bob")
        };

        dbContext.Persons.AddRange(entities);
        await dbContext.SaveChangesAsync();

        // Act
        var results = repository.FindAsync(p => p.Name == "Charlie");

        // Assert
        var resultList = await results.ToListAsync();
        resultList.ShouldBeEmpty();
    }

    [Fact]
    public async Task FindAsync_WithSpecification_ReturnsFilteredEntitiesAsAsyncEnumerable()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        var matchingEntity = new Person(SocialSecurityNumber.Create(123), "Alice");
        var nonMatchingEntity = new Person(SocialSecurityNumber.Create(456), "Bob");

        dbContext.Persons.AddRange(matchingEntity, nonMatchingEntity);
        await dbContext.SaveChangesAsync();

        var spec = new PersonSpecification(x => x.Name == "Alice");

        // Act
        var results = repository.FindAsync(spec);

        // Assert
        var resultList = await results.ToListAsync();
        resultList.ShouldHaveSingleItem();
        resultList.ShouldContain(matchingEntity);
    }

    [Fact]
    public async Task FindAsync_WithSpecification_ReturnsNoEntitiesAsAsyncEnumerable_WhenNoDataMatches()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        dbContext.Persons.Add(new Person(SocialSecurityNumber.Create(123), "Alice"));
        await dbContext.SaveChangesAsync();

        var spec = new PersonSpecification(x => x.Name == "Charlie");

        // Act
        var results = repository.FindAsync(spec);

        // Assert
        var resultList = await results.ToListAsync();
        resultList.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddAsync_AddsEntityToContext()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entity = new Person(SocialSecurityNumber.Create(123), "Alice");

        // Act
        await repository.AddAsync(entity);
        var changes = await dbContext.SaveChangesAsync();

        // Assert
        changes.ShouldBe(1);
        dbContext.Persons.ShouldContain(entity);
    }

    [Fact]
    public async Task AddRangeAsync_AddsMultipleEntitiesToContext()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entities = new List<Person>
        {
            new(SocialSecurityNumber.Create(123), "Alice"),
            new(SocialSecurityNumber.Create(456), "Bob")
        };

        // Act
        await repository.AddRangeAsync(entities);
        var changes = await dbContext.SaveChangesAsync();

        // Assert
        changes.ShouldBe(2);
        entities.All(e => dbContext.Persons.Contains(e)).ShouldBeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenEntityExistsById()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entityId = SocialSecurityNumber.Create(123);
        var entity = new Person(entityId);

        dbContext.Persons.Add(entity);
        await dbContext.SaveChangesAsync();

        // Act
        var exists = await repository.ExistsAsync(entityId);

        // Assert
        exists.ShouldBeTrue();
    }
    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenEntityDoesNotExistById()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entityId = SocialSecurityNumber.Create(123);

        // Act
        var exists = await repository.ExistsAsync(entityId);

        // Assert
        exists.ShouldBeFalse();
    }
    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenEntityExistsByPredicate()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entity = new Person(SocialSecurityNumber.Create(123), "Alice");

        dbContext.Persons.Add(entity);
        await dbContext.SaveChangesAsync();

        // Act
        var exists = await repository.ExistsAsync(e => e.Name == "Alice");

        // Assert
        exists.ShouldBeTrue();
    }
    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenNoEntityMatchesPredicate()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        // Act
        var exists = await repository.ExistsAsync(e => e.Name == "Charlie");

        // Assert
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task Remove_DeletesEntityFromContext()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entity = new Person(SocialSecurityNumber.Create(123));

        dbContext.Persons.Add(entity);
        await dbContext.SaveChangesAsync();

        // Act
        repository.Remove(entity);
        await dbContext.SaveChangesAsync();

        // Assert
        var foundEntity = await dbContext.Persons.FindAsync(entity.Id);
        foundEntity.ShouldBeNull();
    }


    [Fact]
    [SuppressMessage(
        "Major Code Smell",
        "S6966:Awaitable method should be used",
        Justification = "No async overload for remove")]
    public async Task RemoveRange_DeletesMultipleEntitiesFromContext()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entities = new List<Person>
        {
            new(SocialSecurityNumber.Create(123)),
            new(SocialSecurityNumber.Create(456))
        };

        dbContext.Persons.AddRange(entities);
        await dbContext.SaveChangesAsync();

        // Act
        repository.RemoveRange(entities);
        await dbContext.SaveChangesAsync();

        // Assert
        var foundEntities = dbContext.Persons.Where(p => entities.Select(e => e.Id).Contains(p.Id)).ToList();
        foundEntities.ShouldBeEmpty();
    }
}
