using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Repository.Primitives;
using Resrcify.SharedKernel.Repository.UnitTests.Models;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Repository.UnitTests.Primitives;

public class ResultRepositoryTests
{
    private static TestDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    private sealed class TestResultRepository : ResultRepository<TestDbContext, Person, SocialSecurityNumber>
    {
        public TestResultRepository(TestDbContext context) : base(context) { }
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnSuccessResult_WhenEntityExists()
    {
        // Arrange
        var id = SocialSecurityNumber.Create(1);
        var entity = new Person(id, "Rick");
        using var dbContext = CreateDbContext();
        dbContext.Persons.Add(entity);
        await dbContext.SaveChangesAsync();

        var repository = new TestResultRepository(dbContext);

        // Act
        var result = await repository.GetByIdAsync(id);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(entity);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnFailureResult_WhenEntityDoesNotExist()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestResultRepository(dbContext);
        var id = SocialSecurityNumber.Create(999);

        // Act
        var result = await repository.GetByIdAsync(id);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldContain(e =>
            e.Code == "Person.NotFound" &&
            e.Type == ErrorType.NotFound);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithPredicate_ShouldReturnSuccessResult_WhenMatchExists()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestResultRepository(dbContext);
        var entity = new Person(SocialSecurityNumber.Create(42), "Alice");
        dbContext.Persons.Add(entity);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await repository.FirstOrDefaultAsync(p => p.Name == "Alice");

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(entity);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithPredicate_ShouldReturnFailureResult_WhenNoMatch()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestResultRepository(dbContext);

        // Act
        var result = await repository.FirstOrDefaultAsync(p => p.Name == "Nonexistent");

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldContain(e => e.Code == "Person.NotFound");
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithSpecification_ShouldReturnSuccessResult_WhenMatchExists()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestResultRepository(dbContext);
        var entity = new Person(SocialSecurityNumber.Create(101), "Bob");
        dbContext.Persons.Add(entity);
        await dbContext.SaveChangesAsync();

        var spec = new PersonSpecification(p => p.Name == "Bob");

        // Act
        var result = await repository.FirstOrDefaultAsync(spec);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(entity);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithSpecification_ShouldReturnFailureResult_WhenNoMatch()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var repository = new TestResultRepository(dbContext);

        var spec = new PersonSpecification(p => p.Name == "Nobody");

        // Act
        var result = await repository.FirstOrDefaultAsync(spec);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldContain(e => e.Code == "Person.NotFound");
    }
}
