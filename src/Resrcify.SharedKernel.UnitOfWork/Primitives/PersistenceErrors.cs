using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.Primitives;

/// <summary>
/// The errors <see cref="UnitOfWork{TDbContext}.TryCompleteAsync"/> returns for a save the database refused in a way the
/// caller can answer. Match on <see cref="Error.Code"/> (e.g. <c>PersistenceErrors.Concurrency.Code</c>): the error
/// returned for a not-null, check, foreign-key or too-long violation also names the constraint, the table and the column
/// in its message, when the database driver reports them, so its message differs from the field's.
/// </summary>
public static class PersistenceErrors
{
    /// <summary>PostgreSQL's (and the SQL standard's) <c>SQLSTATE</c> for a unique-constraint violation.</summary>
    public const string UniqueViolationSqlState = "23505";

    /// <summary>The <c>SQLSTATE</c> for a <c>NOT NULL</c> violation (a required column left empty).</summary>
    public const string NotNullViolationSqlState = "23502";

    /// <summary>The <c>SQLSTATE</c> for a foreign-key violation.</summary>
    public const string ForeignKeyViolationSqlState = "23503";

    /// <summary>
    /// The <c>SQLSTATE</c> for deleting (or changing the key of) a row a foreign key declared <c>ON DELETE RESTRICT</c>
    /// still refers to, as EF Core's <c>DeleteBehavior.Restrict</c> declares it (a <c>NO ACTION</c> key raises
    /// <see cref="ForeignKeyViolationSqlState"/>).
    /// </summary>
    public const string RestrictViolationSqlState = "23001";

    /// <summary>The <c>SQLSTATE</c> for a check-constraint violation.</summary>
    public const string CheckViolationSqlState = "23514";

    /// <summary>The <c>SQLSTATE</c> for a string longer than its column allows ("string data, right truncation").</summary>
    public const string ValueTooLongSqlState = "22001";

    /// <summary>The <c>SQLSTATE</c> for a serialization failure (another transaction changed what this one read).</summary>
    public const string SerializationFailureSqlState = "40001";

    /// <summary>The <c>SQLSTATE</c> PostgreSQL uses for a deadlock.</summary>
    public const string DeadlockSqlState = "40P01";

    // The keys under which a driver's exception holds the names a violation is about, in Exception.Data. Npgsql's
    // PostgresException puts there each field PostgreSQL reported (also its properties of the same names); UnitOfWork
    // references no provider, so it reads them from there.
    private const string ConstraintNameKey = "ConstraintName";
    private const string TableNameKey = "TableName";
    private const string ColumnNameKey = "ColumnName";

    /// <summary>
    /// A row changed (or was deleted) since it was read: its concurrency token no longer matches. A conflict to an HTTP
    /// caller (409), but transient (<c>Error.IsTransient()</c>) to the message bus, which retries it in a new scope.
    /// </summary>
    public static readonly Error Concurrency = Error.Conflict(
        ErrorTypeExtensions.ConcurrencyConflictCode,
        "The data was changed by someone else since it was read. Read it again and retry.");

    /// <summary>The save would duplicate a value that must be unique.</summary>
    public static readonly Error UniqueViolation = Error.Conflict(
        "Persistence.UniqueViolation",
        "The data conflicts with existing data that must be unique.");

    /// <summary>
    /// A value the database requires was left empty (<c>SQLSTATE 23502</c>): a <c>Validation</c> error (400). The error
    /// returned names the table and the column, when the driver reports them.
    /// </summary>
    public static readonly Error NotNullViolation = Error.Validation(
        "Persistence.NotNullViolation",
        "A value the database requires is missing.");

    /// <summary>
    /// A value breaks a check constraint (<c>SQLSTATE 23514</c>): a <c>Validation</c> error (400). The error returned
    /// names the constraint and the table, when the driver reports them.
    /// </summary>
    public static readonly Error CheckViolation = Error.Validation(
        "Persistence.CheckViolation",
        "A value breaks a rule the database enforces.");

    /// <summary>
    /// A string is longer than its column allows (<c>SQLSTATE 22001</c>): a <c>Validation</c> error (400). PostgreSQL
    /// doesn't report the column, so the error returned is this one.
    /// </summary>
    public static readonly Error ValueTooLong = Error.Validation(
        "Persistence.ValueTooLong",
        "A value is longer than the database allows.");

    /// <summary>
    /// The save refers to a row that doesn't exist, or deletes one that other rows still refer to (<c>SQLSTATE
    /// 23503</c>, or <c>23001</c> for an <c>ON DELETE RESTRICT</c> key): a <c>Conflict</c> (409), since it depends on what
    /// else is in the database. The error returned names the constraint and the table, when the driver reports them.
    /// </summary>
    public static readonly Error ForeignKeyViolation = Error.Conflict(
        "Persistence.ForeignKeyViolation",
        "The data refers to data that doesn't exist, or other data still refers to it.");

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

        var database = FindDatabaseException(exception);
        error = database?.SqlState switch
        {
            UniqueViolationSqlState => UniqueViolation,
            NotNullViolationSqlState => Naming(NotNullViolation, database),
            CheckViolationSqlState => Naming(CheckViolation, database),
            ValueTooLongSqlState => Naming(ValueTooLong, database),
            ForeignKeyViolationSqlState => Naming(ForeignKeyViolation, database),
            RestrictViolationSqlState => Naming(ForeignKeyViolation, database),
            SerializationFailureSqlState when !inTransaction => SerializationFailure,
            DeadlockSqlState when !inTransaction => Deadlock,
            _ => Error.None,
        };

        return error != Error.None;
    }

    /// <summary>
    /// The database exception behind <paramref name="exception"/> that carries a <c>SQLSTATE</c>: itself, or the first
    /// one among its causes, e.g. under a <see cref="DbUpdateException"/>, a <see cref="RetryLimitExceededException"/> (a
    /// retrying execution strategy that gave up), or the <see cref="InvalidOperationException"/> a non-retrying Npgsql
    /// strategy wraps a transient failure in.
    /// </summary>
    private static DbException? FindDatabaseException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: not null } database)
                return database;
        }

        return null;
    }

    // The error with the names the violation is about, when the driver reported any:
    // A value the database requires is missing (table "Shards", column "Name").
    // Names only, never a value: PostgreSQL's detail ("Key (x)=(y) is not present ...") holds the data, so it isn't read.
    private static Error Naming(
        Error error,
        DbException database)
    {
        var names = NamesIn(database);
        if (names.Count == 0)
            return error;

        var message = $"{error.Message.TrimEnd('.')} ({string.Join(", ", names)}).";
        return new Error(
            error.Code,
            message,
            error.Type);
    }

    private static List<string> NamesIn(DbException database)
    {
        var names = new List<string>(3);
        AddName(names, "constraint", database, ConstraintNameKey);
        AddName(names, "table", database, TableNameKey);
        AddName(names, "column", database, ColumnNameKey);
        return names;
    }

    private static void AddName(
        List<string> names,
        string kind,
        DbException database,
        string key)
    {
        if (database.Data[key] is string { Length: > 0 } name)
            names.Add($"{kind} \"{name}\"");
    }
}
