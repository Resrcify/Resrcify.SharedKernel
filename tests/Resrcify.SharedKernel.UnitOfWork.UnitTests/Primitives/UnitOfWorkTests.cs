using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;

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
        fetchedPerson!.ShouldNotBeNull();
        fetchedPerson!.Name.ShouldBe("John Doe");
    }


    [Fact]
    public async Task CommitTransactionAsync_ShouldPersistChanges()
    {
        // Arrange
        await DbContext.Database.BeginTransactionAsync();
        var person = new Person(SocialSecurityNumber.Create(987654321), "Jane Doe");
        DbContext.Persons.Add(person);

        // Act
        await UnitOfWork.CompleteAsync();
        await UnitOfWork.CommitTransactionAsync();


        // Assert
        var fetchedPerson = await DbContext.Persons.SingleOrDefaultAsync();
        fetchedPerson.ShouldNotBeNull();
        fetchedPerson!.Name.ShouldBe("Jane Doe");
    }

    [Fact]
    public async Task RollbackTransactionAsync_ShouldNotPersistChanges()
    {
        // Arrange
        await DbContext.Database.BeginTransactionAsync();
        var person = new Person(SocialSecurityNumber.Create(112233445), "Alice");
        DbContext.Persons.Add(person);

        // Act
        await UnitOfWork.CompleteAsync();
        await UnitOfWork.RollbackTransactionAsync();

        // Assert
        var fetchedPerson = await DbContext.Persons.SingleOrDefaultAsync();
        fetchedPerson.ShouldBeNull();
    }
}