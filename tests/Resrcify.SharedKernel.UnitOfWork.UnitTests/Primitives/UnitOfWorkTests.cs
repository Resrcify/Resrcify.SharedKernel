using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FluentAssertions;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Primitives;

public class UnitOfWorkTests : DbSetupBase
{
    [Fact]
    public async Task CompleteAsync_ShouldPersistChanges()
    {
        // Arrange
        var person = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        DbContext.Persons.Add(person);

        // Act
        await UnitOfWork.CompleteAsync();

        // Assert
        var fetchedPerson = await DbContext.Persons.SingleOrDefaultAsync();
        fetchedPerson!.Should().NotBeNull();
        fetchedPerson!.Name.Should().Be("John Doe");
    }


    [Fact]
    public async Task CommitTransactionAsync_ShouldPersistChanges()
    {
        // Arrange
        using var transaction = await DbContext.Database.BeginTransactionAsync();
        var person = new Person(SocialSecurityNumber.Create(987654321), "Jane Doe");
        DbContext.Persons.Add(person);

        // Act
        await UnitOfWork.CompleteAsync();
        await UnitOfWork.CommitTransactionAsync();


        // Assert
        var fetchedPerson = await DbContext.Persons.SingleOrDefaultAsync();
        fetchedPerson.Should().NotBeNull();
        fetchedPerson!.Name.Should().Be("Jane Doe");
    }

    [Fact]
    public async Task RollbackTransactionAsync_ShouldNotPersistChanges()
    {
        // Arrange
        using var transaction = await DbContext.Database.BeginTransactionAsync();
        var person = new Person(SocialSecurityNumber.Create(112233445), "Alice");
        DbContext.Persons.Add(person);

        // Act
        await UnitOfWork.CompleteAsync();
        await UnitOfWork.RollbackTransactionAsync();

        // Assert
        var fetchedPerson = await DbContext.Persons.SingleOrDefaultAsync();
        fetchedPerson.Should().BeNull();
    }
}