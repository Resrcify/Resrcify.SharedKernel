using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.Primitives;

/// <summary>
/// The errors <see cref="UnitOfWork{TDbContext}.TryCompleteAsync"/> returns for a save the database refused in a way the
/// caller can answer. Match on <see cref="Error.Code"/> (e.g. <c>PersistenceErrors.Concurrency.Code</c>).
/// </summary>
public static class PersistenceErrors
{
    /// <summary>PostgreSQL's (and the SQL standard's) <c>SQLSTATE</c> for a unique-constraint violation.</summary>
    public const string UniqueViolationSqlState = "23505";

    /// <summary>The <c>SQLSTATE</c> for a serialization failure (another transaction changed what this one read).</summary>
    public const string SerializationFailureSqlState = "40001";

    /// <summary>The <c>SQLSTATE</c> PostgreSQL uses for a deadlock.</summary>
    public const string DeadlockSqlState = "40P01";

    /// <summary>A row changed (or was deleted) since it was read: its concurrency token no longer matches.</summary>
    public static readonly Error Concurrency = Error.Conflict(
        "Persistence.Concurrency",
        "The data was changed by someone else since it was read. Read it again and retry.");

    /// <summary>The save would duplicate a value that must be unique.</summary>
    public static readonly Error UniqueViolation = Error.Conflict(
        "Persistence.UniqueViolation",
        "The data conflicts with existing data that must be unique.");

    /// <summary>The database couldn't order this save with a concurrent one; another try may pass.</summary>
    public static readonly Error SerializationFailure = Error.Failure(
        "Persistence.SerializationFailure",
        "The save collided with a concurrent one. Try again.");

    /// <summary>The save deadlocked with a concurrent one and was cancelled; another try may pass.</summary>
    public static readonly Error Deadlock = Error.Failure(
        "Persistence.Deadlock",
        "The save deadlocked with a concurrent one. Try again.");

    /// <summary>
    /// The error for <paramref name="exception"/>, when it is one a caller can answer. A serialization failure or a
    /// deadlock inside a transaction is left to throw: it aborted the whole transaction, so only the transaction's owner
    /// (an execution strategy that retries it) can do something about it.
    /// </summary>
    internal static bool TryGetError(
        Exception exception,
        bool inTransaction,
        out Error error)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            error = Concurrency;
            return true;
        }

        var sqlState = FindSqlState(exception);
        switch (sqlState)
        {
            case UniqueViolationSqlState:
                error = UniqueViolation;
                return true;
            case SerializationFailureSqlState when !inTransaction:
                error = SerializationFailure;
                return true;
            case DeadlockSqlState when !inTransaction:
                error = Deadlock;
                return true;
            default:
                error = Error.None;
                return false;
        }
    }

    /// <summary>
    /// The <c>SQLSTATE</c> of the database exception behind <paramref name="exception"/>: itself, the cause of a
    /// <see cref="DbUpdateException"/>, or of a <see cref="RetryLimitExceededException"/> (a retrying execution strategy
    /// that gave up).
    /// </summary>
    private static string? FindSqlState(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: { } sqlState })
                return sqlState;
            if (current is not (DbUpdateException or RetryLimitExceededException))
                return null;
        }

        return null;
    }
}
