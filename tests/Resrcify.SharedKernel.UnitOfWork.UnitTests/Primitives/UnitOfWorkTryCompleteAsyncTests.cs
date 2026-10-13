using System;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Resrcify.SharedKernel.UnitOfWork.Primitives;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Primitives;

/// <summary>
/// <c>TryCompleteAsync</c> with the failure a save raises staged by an interceptor, since SQLite doesn't raise
/// PostgreSQL's (the Postgres integration tests raise the real ones).
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class UnitOfWorkTryCompleteAsyncTests : DbSetupBase
{
    private readonly FailingSave _failure;

    public UnitOfWorkTryCompleteAsyncTests()
        : this(new FailingSave())
    {
    }

    private UnitOfWorkTryCompleteAsyncTests(FailingSave failure)
        : base(failure)
        => _failure = failure;

    [Fact]
    public async Task TryCompleteAsync_ShouldSaveAndSucceed_WhenTheSaveSucceeds()
    {
        DbContext.Persons.Add(new Person(SocialSecurityNumber.Create(300000001), "TrySaved"));

        var result = await UnitOfWork.TryCompleteAsync();

        result.IsSuccess.ShouldBeTrue();
        DbContext.ChangeTracker.Clear();
        (await DbContext.Persons.AnyAsync(person => person.Name == "TrySaved")).ShouldBeTrue();
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldReturnAConflict_ForAConcurrencyException()
    {
        _failure.Exception = new DbUpdateConcurrencyException("stale");
        DbContext.Persons.Add(new Person(SocialSecurityNumber.Create(300000002)));

        var result = await UnitOfWork.TryCompleteAsync();

        result.Errors.ShouldHaveSingleItem().ShouldBe(PersistenceErrors.Concurrency);
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldReturnATransientFailure_ForASerializationFailureOutsideATransaction()
    {
        _failure.Exception = new DbUpdateException("save failed", DbExceptions.WithSqlState("40001"));
        DbContext.Persons.Add(new Person(SocialSecurityNumber.Create(300000003)));

        var result = await UnitOfWork.TryCompleteAsync();

        result.Errors.ShouldHaveSingleItem().ShouldBe(PersistenceErrors.SerializationFailure);
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldThrowASerializationFailure_InsideATransaction_ForItsOwnerToRetry()
    {
        _failure.Exception = new DbUpdateException("save failed", DbExceptions.WithSqlState("40001"));
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        DbContext.Persons.Add(new Person(SocialSecurityNumber.Create(300000004)));

        await Should.ThrowAsync<DbUpdateException>(() => UnitOfWork.TryCompleteAsync());
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldStopTrackingTheRefusedChanges_SoTheNextSaveDoesntSendThemAgain()
    {
        var saved = new Person(SocialSecurityNumber.Create(300000006), "Saved");
        DbContext.Persons.Add(saved);
        await UnitOfWork.CompleteAsync();
        _failure.Exception = new DbUpdateException("save failed", DbExceptions.WithSqlState("23505"));
        var refused = new Person(SocialSecurityNumber.Create(300000007), "Refused");
        DbContext.Persons.Add(refused);
        saved.Rename("Renamed");

        var result = await UnitOfWork.TryCompleteAsync();

        result.Errors.ShouldHaveSingleItem().ShouldBe(PersistenceErrors.UniqueViolation);
        DbContext.Entry(refused).State.ShouldBe(EntityState.Detached);
        DbContext.Entry(saved).State.ShouldBe(EntityState.Unchanged);
        saved.Name.ShouldBe("Saved");
        saved.GetDomainEvents().ShouldBeEmpty();

        _failure.Exception = null;
        DbContext.Persons.Add(new Person(SocialSecurityNumber.Create(300000008), "Next"));
        await UnitOfWork.CompleteAsync();
        DbContext.ChangeTracker.Clear();
        (await DbContext.Persons.Select(person => person.Name).ToListAsync()).Order().ShouldBe(["Next", "Saved"]);
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldThrow_WhatACallerCantAnswer()
    {
        _failure.Exception = new InvalidOperationException("broken");
        DbContext.Persons.Add(new Person(SocialSecurityNumber.Create(300000005)));

        await Should.ThrowAsync<InvalidOperationException>(() => UnitOfWork.TryCompleteAsync());
    }

    /// <summary>Throws <see cref="Exception"/> when a save starts, when one is set.</summary>
    private sealed class FailingSave : SaveChangesInterceptor
    {
        public Exception? Exception { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => Exception is { } exception
                ? ValueTask.FromException<InterceptionResult<int>>(exception)
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
