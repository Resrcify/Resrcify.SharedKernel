using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Primitives;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class PersistenceErrorsTests
{
    public static TheoryData<string, Exception, bool, string?> Exceptions => new()
    {
        { "concurrency", new DbUpdateConcurrencyException("stale"), false, "Persistence.Concurrency" },
        { "concurrency in a transaction", new DbUpdateConcurrencyException("stale"), true, "Persistence.Concurrency" },
        { "unique", Save(Database("23505")), false, "Persistence.UniqueViolation" },
        { "unique in a transaction", Save(Database("23505")), true, "Persistence.UniqueViolation" },
        { "serialization", Save(Database("40001")), false, "Persistence.SerializationFailure" },
        { "serialization in a transaction", Save(Database("40001")), true, null },
        { "deadlock", Save(Database("40P01")), false, "Persistence.Deadlock" },
        { "deadlock in a transaction", Save(Database("40P01")), true, null },
        { "a commit's serialization failure", Database("40001"), false, "Persistence.SerializationFailure" },
        { "retries given up", new RetryLimitExceededException("gave up", Save(Database("40001"))), false, "Persistence.SerializationFailure" },
        { "another constraint", Save(Database("23503")), false, null },
        { "no SQLSTATE", Save(Database(null)), false, null },
        { "a database error wrapped in something else", new InvalidOperationException("wrapped", Database("23505")), false, "Persistence.UniqueViolation" },
        { "a deadlock the Npgsql strategy wrapped", new InvalidOperationException("likely transient", Save(Database("40P01"))), false, "Persistence.Deadlock" },
        { "a serialization failure the Npgsql strategy wrapped", new InvalidOperationException("likely transient", Database("40001")), false, "Persistence.SerializationFailure" },
        { "a wrapped deadlock in a transaction", new InvalidOperationException("likely transient", Database("40P01")), true, null },
        { "anything else", new InvalidOperationException("broken"), false, null },
    };

    [Theory]
    [MemberData(nameof(Exceptions))]
    public void TryGetError_ShouldMapWhatACallerCanAnswer_AndNothingElse(
        string description,
        Exception exception,
        bool inTransaction,
        string? expectedCode)
    {
        var mapped = PersistenceErrors.TryGetError(exception, inTransaction, out var error);

        mapped.ShouldBe(expectedCode is not null, description);
        if (expectedCode is not null)
            error.Code.ShouldBe(expectedCode, description);
    }

    [Fact]
    public void Errors_ShouldBeConflictsForTheCallersData_AndTransientFailuresForCollisions()
    {
        var expected = new Dictionary<Error, ErrorType>
        {
            [PersistenceErrors.Concurrency] = ErrorType.Conflict,
            [PersistenceErrors.UniqueViolation] = ErrorType.Conflict,
            [PersistenceErrors.SerializationFailure] = ErrorType.Failure,
            [PersistenceErrors.Deadlock] = ErrorType.Failure,
        };

        foreach (var (error, type) in expected)
            error.Type.ShouldBe(type, error.Code);
        PersistenceErrors.SerializationFailure.Type.IsTransient().ShouldBeTrue();
        PersistenceErrors.Concurrency.Type.IsTransient().ShouldBeFalse();
    }

    private static DbUpdateException Save(DbException cause)
        => new("save failed", cause);

    private static DbException Database(string? sqlState)
        => DbExceptions.WithSqlState(sqlState);
}
