using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Primitives;

/// <summary>
/// <c>ExecuteAsync</c>: a failed operation's changes are undone in the change tracker, so the scope's next save doesn't
/// save half of it; changes pending before it are left alone.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class UnitOfWorkExecuteAsyncTests : DbSetupBase
{
    [Fact]
    public async Task ExecuteAsync_ShouldUndoWhatTheOperationChanged_WhenItReturnsAFailure()
    {
        var withdrawnFrom = await SavedAsync(400000001, "Alice");
        var depositedTo = await SavedAsync(400000002, "Bob");

        var result = await UnitOfWork.ExecuteAsync(
            _ =>
            {
                withdrawnFrom.Rename("Alice-10");
                DbContext.Persons.Add(new Person(SocialSecurityNumber.Create(400000003), "Carol"));
                return Task.FromResult(Result.Failure(Error.Failure("Deposit.Failed", "The deposit failed.")));
            });

        result.IsFailure.ShouldBeTrue();
        DbContext.Entry(withdrawnFrom).State.ShouldBe(EntityState.Unchanged);
        withdrawnFrom.Name.ShouldBe("Alice");
        withdrawnFrom.GetDomainEvents().ShouldBeEmpty();
        DbContext.ChangeTracker.Entries<Person>().Select(entry => entry.Entity).ShouldBe([withdrawnFrom, depositedTo], ignoreOrder: true);

        await UnitOfWork.CompleteAsync();
        (await NamesInTheDatabaseAsync()).ShouldBe(["Alice", "Bob"]);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUndoWhatTheOperationChanged_AndRethrow_WhenItThrows()
    {
        var person = await SavedAsync(400000011, "Dave");

        await Should.ThrowAsync<InvalidOperationException>(() => UnitOfWork.ExecuteAsync<Result>(
            _ =>
            {
                person.Rename("Dave2");
                throw new InvalidOperationException("The handler failed.");
            }));

        person.Name.ShouldBe("Dave");
        person.GetDomainEvents().ShouldBeEmpty();
        DbContext.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepTheChangesPendingBefore_AndDropOnlyTheEventsTheOperationRaised()
    {
        var pendingBefore = await SavedAsync(400000021, "Erin");
        pendingBefore.Rename("Erin2");
        var raisedBefore = pendingBefore.GetDomainEvents().ShouldHaveSingleItem();
        var addedBefore = new Person(SocialSecurityNumber.Create(400000022), "Frank");
        DbContext.Persons.Add(addedBefore);

        await UnitOfWork.ExecuteAsync(
            _ =>
            {
                pendingBefore.Rename("Erin3");
                return Task.FromResult(Result.Failure(Error.Failure("Command.Failed", "The command failed.")));
            });

        DbContext.Entry(addedBefore).State.ShouldBe(EntityState.Added);
        DbContext.Entry(pendingBefore).State.ShouldBe(EntityState.Modified);
        pendingBefore.GetDomainEvents().ShouldHaveSingleItem().ShouldBeSameAs(raisedBefore);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLeaveTheOperationsChanges_WhenItSucceeds()
    {
        var person = await SavedAsync(400000031, "Gina");

        var result = await UnitOfWork.ExecuteAsync(
            _ =>
            {
                person.Rename("Gina2");
                return Task.FromResult(Result.Success());
            });

        result.IsSuccess.ShouldBeTrue();
        DbContext.Entry(person).State.ShouldBe(EntityState.Modified);
        person.GetDomainEvents().ShouldHaveSingleItem();
    }

    private async Task<Person> SavedAsync(int id, string name)
    {
        var person = new Person(SocialSecurityNumber.Create(id), name);
        DbContext.Persons.Add(person);
        await UnitOfWork.CompleteAsync();
        return person;
    }

    private async Task<string[]> NamesInTheDatabaseAsync()
        => [.. (await DbContext.Persons.AsNoTracking().Select(person => person.Name).ToListAsync()).Order()];
}
