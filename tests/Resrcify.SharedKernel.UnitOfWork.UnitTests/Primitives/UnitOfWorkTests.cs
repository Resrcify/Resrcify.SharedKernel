using System;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
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
        fetchedPerson.Name.ShouldBe("John Doe");
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
        fetchedPerson.Name.ShouldBe("Jane Doe");
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

    [Fact]
    public async Task ExecuteInTransactionAsync_ShouldCommitChanges_WhenOperationSucceeds()
    {
        // Act
        await UnitOfWork.ExecuteInTransactionAsync(_ =>
        {
            DbContext.Persons.Add(
                new Person(SocialSecurityNumber.Create(200000001), "Committed"));
            return Task.CompletedTask;
        });

        // Assert
        DbContext.ChangeTracker.Clear();
        var fetchedPerson = await DbContext.Persons.SingleOrDefaultAsync();
        fetchedPerson.ShouldNotBeNull();
        fetchedPerson.Name.ShouldBe("Committed");
        DbContext.Database.CurrentTransaction.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ShouldRollBackEverything_WhenOperationThrows()
    {
        // Act — the operation persists a row and then fails.
        var act = async () => await UnitOfWork.ExecuteInTransactionAsync(_ =>
        {
            DbContext.Persons.Add(
                new Person(SocialSecurityNumber.Create(200000002), "RolledBack"));
            throw new InvalidOperationException("operation failed");
        });

        // Assert — the exception surfaces and nothing the operation did was kept.
        await Should.ThrowAsync<InvalidOperationException>(act);

        DbContext.ChangeTracker.Clear();
        var fetchedPerson = await DbContext.Persons.SingleOrDefaultAsync();
        fetchedPerson.ShouldBeNull();
        DbContext.Database.CurrentTransaction.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ShouldDisposeTransaction_AllowingConsecutiveCalls()
    {
        // Act — a second call only works if the first transaction was disposed
        // (a dangling transaction would make the next BeginTransaction throw).
        await UnitOfWork.ExecuteInTransactionAsync(_ =>
        {
            DbContext.Persons.Add(
                new Person(SocialSecurityNumber.Create(200000003), "First"));
            return Task.CompletedTask;
        });
        await UnitOfWork.ExecuteInTransactionAsync(_ =>
        {
            DbContext.Persons.Add(
                new Person(SocialSecurityNumber.Create(200000004), "Second"));
            return Task.CompletedTask;
        });

        // Assert
        DbContext.ChangeTracker.Clear();
        var count = await DbContext.Persons.CountAsync();
        count.ShouldBe(2);
        DbContext.Database.CurrentTransaction.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WithResult_ShouldCommit_WhenResultIsSuccess()
    {
        // Act
        var result = await UnitOfWork.ExecuteInTransactionAsync(_ =>
        {
            DbContext.Persons.Add(
                new Person(SocialSecurityNumber.Create(200000005), "Committed"));
            return Task.FromResult(Result.Success());
        });

        // Assert
        result.IsSuccess.ShouldBeTrue();
        DbContext.ChangeTracker.Clear();
        (await DbContext.Persons.SingleOrDefaultAsync()).ShouldNotBeNull();
        DbContext.Database.CurrentTransaction.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WithResult_ShouldRollBack_WhenResultIsFailure()
    {
        // Act — the operation persists a row but returns a failure result.
        var result = await UnitOfWork.ExecuteInTransactionAsync(_ =>
        {
            DbContext.Persons.Add(
                new Person(SocialSecurityNumber.Create(200000006), "RolledBack"));
            return Task.FromResult(Result.Failure(Error.NullValue));
        });

        // Assert — failure result is returned and its work is rolled back.
        result.IsFailure.ShouldBeTrue();
        DbContext.ChangeTracker.Clear();
        (await DbContext.Persons.SingleOrDefaultAsync()).ShouldBeNull();
        DbContext.Database.CurrentTransaction.ShouldBeNull();
    }
}