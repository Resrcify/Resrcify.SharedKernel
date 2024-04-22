using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.GenericRepository.Specifications;
using Resrcify.SharedKernel.GenericRepository.UnitTests.Models;
using Xunit;

namespace Resrcify.SharedKernel.GenericRepository.UnitTests.GenericRepository;

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
            .Should()
            .NotBeNull();

        result
            .Should()
            .BeEquivalentTo(entity);
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
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_CancelsWhenRequested()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        var person = new Person(SocialSecurityNumber.Create(123));
        dbContext.Persons.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.Entry(person).State = EntityState.Detached;
        using var cancellationTokenSource = new CancellationTokenSource();

        // Act
        var task = Task.Run(() => repository.GetByIdAsync(person.Id, cancellationTokenSource.Token));
        await cancellationTokenSource.CancelAsync();

        // Assert
        Func<Task> act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
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
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(entity, options => options.ComparingByMembers<Person>());
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
        result.Should().BeNull();
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
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(firstEntity, options => options.ComparingByMembers<Person>());
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
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(entity, options => options.ComparingByMembers<Person>());
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
        result.Should().BeNull();
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithSpecification_CancelsWhenRequested()
    {
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        var person = new Person(SocialSecurityNumber.Create(123));
        dbContext.Persons.Add(person);
        await dbContext.SaveChangesAsync();
        var spec = new PersonSpecification(p => p.Id.Value == 123);
        using var cancellationTokenSource = new CancellationTokenSource();

        // Act
        var task = Task.Run(() => repository.FirstOrDefaultAsync(spec, cancellationTokenSource.Token));
        await cancellationTokenSource.CancelAsync();

        // Assert
        Func<Task> act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithPredicate_CancelsWhenRequested()
    {
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        var person = new Person(SocialSecurityNumber.Create(123));
        dbContext.Persons.Add(person);
        await dbContext.SaveChangesAsync();

        using var cancellationTokenSource = new CancellationTokenSource();

        // Act
        var task = Task.Run(() => repository.FirstOrDefaultAsync(x => x.Id == person.Id, cancellationTokenSource.Token));
        await cancellationTokenSource.CancelAsync();

        // Assert
        Func<Task> act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
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
        resultList.Should().HaveCount(entities.Count);
        resultList.Should().BeEquivalentTo(entities, options => options.ComparingByMembers<Person>());
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
        resultList.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllEntitiesAsEnumerable()
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
        var results = await repository.GetAllAsync(CancellationToken.None);

        // Assert
        results.Should().HaveCount(entities.Count);
        results.Should().BeEquivalentTo(entities, options => options.ComparingByMembers<Person>());
    }

    [Fact]
    public async Task GetAllAsync_ReturnsNoEntitiesAsEnumerable_WhenDatabaseIsEmpty()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        // Act
        var results = await repository.GetAllAsync(CancellationToken.None);

        // Assert
        results.Should().BeEmpty(); // Ensure the list is empty
    }

    [Fact]
    public async Task GetAllAsync_CancelsWhenRequested()
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

        using var cancellationTokenSource = new CancellationTokenSource();

        // Act
        var task = Task.Run(() => repository.GetAllAsync(cancellationTokenSource.Token));

        await cancellationTokenSource.CancelAsync();

        // Assert
        Func<Task> act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
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
        resultList.Should().ContainSingle();
        resultList.Should().Contain(matchingEntity);
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
        resultList.Should().BeEmpty();
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
        resultList.Should().ContainSingle();
        resultList.Should().Contain(matchingEntity);
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
        resultList.Should().BeEmpty();
    }

    [Fact]
    public async Task FindAsync_ReturnsFilteredEntitiesAsEnumerable_WhenPredicateMatches()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        var matchingEntity = new Person(SocialSecurityNumber.Create(123), "Alice");
        var nonMatchingEntity = new Person(SocialSecurityNumber.Create(456), "Bob");

        dbContext.Persons.AddRange(matchingEntity, nonMatchingEntity);
        await dbContext.SaveChangesAsync();

        // Act
        var results = await repository.FindAsync(p => p.Name == "Alice", CancellationToken.None);

        // Assert
        results.Should().ContainSingle();
        results.Should().Contain(matchingEntity);
    }
    [Fact]
    public async Task FindAsync_ReturnsNoEntitiesAsEnumerable_WhenNoDataMatchesPredicate()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        dbContext.Persons.Add(new Person(SocialSecurityNumber.Create(123), "Alice"));
        await dbContext.SaveChangesAsync();

        // Act
        var results = await repository.FindAsync(p => p.Name == "Charlie", CancellationToken.None);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task FindAsync_CancelsWhenRequested_WhenNotUsingSpecification()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        dbContext.Persons.Add(new Person(SocialSecurityNumber.Create(123), "Alice"));
        await dbContext.SaveChangesAsync();

        using var cancellationTokenSource = new CancellationTokenSource();

        // Act
        var task = Task.Run(() => repository.FindAsync(p => p.Name == "Alice", cancellationTokenSource.Token));

        await cancellationTokenSource.CancelAsync();

        // Assert
        Func<Task> act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
    [Fact]
    public async Task FindAsync_WithSpecification_ReturnsFilteredEntitiesAsEnumerable()
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
        var results = await repository.FindAsync(spec, CancellationToken.None);

        // Assert
        results.Should().ContainSingle();
        results.Should().Contain(matchingEntity);
    }
    [Fact]
    public async Task FindAsync_WithSpecification_ReturnsNoEntitiesAsEnumerable_WhenNoDataMatches()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        var spec = new PersonSpecification(x => x.Name == "Charlie");

        // Act
        var results = await repository.FindAsync(spec, CancellationToken.None);

        // Assert
        results.Should().BeEmpty();
    }
    [Fact]
    public async Task FindAsync_WithSpecification_CancelsWhenRequested()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);

        dbContext.Persons.Add(new Person(SocialSecurityNumber.Create(123), "Alice"));
        await dbContext.SaveChangesAsync();

        var spec = new PersonSpecification(x => x.Name == "Alice");
        using var cancellationTokenSource = new CancellationTokenSource();

        // Act
        var task = Task.Run(() => repository.FindAsync(spec, cancellationTokenSource.Token));
        await cancellationTokenSource.CancelAsync();

        // Assert
        Func<Task> act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
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
        changes.Should().Be(1);
        dbContext.Persons.Should().Contain(entity);
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
        changes.Should().Be(2);
        dbContext.Persons.Should().Contain(entities);
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
        exists.Should().BeTrue();
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
        exists.Should().BeFalse();
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
        exists.Should().BeTrue();
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
        exists.Should().BeFalse();
    }
    [Fact]
    public async Task ExistsAsync_ById_CancelsWhenRequested()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entityId = SocialSecurityNumber.Create(123);
        var entity = new Person(entityId);

        dbContext.Persons.Add(entity);
        await dbContext.SaveChangesAsync();
        using var cancellationTokenSource = new CancellationTokenSource();
        // Act
        var task = Task.Run(() => repository.ExistsAsync(entityId, cancellationTokenSource.Token));
        await cancellationTokenSource.CancelAsync();

        // Assert
        Func<Task> act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExistsAsync_ByPredicate_CancelsWhenRequested()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestRepository(dbContext);
        var entityId = SocialSecurityNumber.Create(123);
        var entity = new Person(entityId);

        dbContext.Persons.Add(entity);
        await dbContext.SaveChangesAsync();
        using var cancellationTokenSource = new CancellationTokenSource();
        // Act
        var task = Task.Run(() => repository.ExistsAsync(x => x.Id == entityId, cancellationTokenSource.Token));
        await cancellationTokenSource.CancelAsync();

        // Assert
        Func<Task> act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
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
        foundEntity.Should().BeNull();
    }

    [Fact]
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
        foundEntities.Should().BeEmpty();
    }
}