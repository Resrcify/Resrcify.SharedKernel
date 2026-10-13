using System;
using System.Data.Common;

namespace Resrcify.SharedKernel.UnitOfWork.Migrations;

/// <summary>
/// When <see cref="MigrateOnStartupService{TContext}"/> migrates again: after losing a race to create something another
/// instance created at the same moment, while several instances migrate a database that doesn't exist yet.
/// </summary>
/// <remarks>
/// <para>
/// EF Core creates what it needs before it takes its migration lock (EF Core 10 <c>Migrator.MigrateAsync</c>): the
/// database (<c>IDatabaseCreator.ExistsAsync</c>, then <c>CreateAsync</c>), then the history table and its schema
/// (<c>IHistoryRepository.CreateIfNotExistsAsync</c>), and only then, in the migration transaction,
/// <c>LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE</c> (Npgsql). Two instances can both find the database
/// missing and both create it: Npgsql 10 ignores the unique violation on <c>pg_database</c> but not
/// <c>42P04 duplicate_database</c>, which the slower one gets when the faster one has already committed (measured on
/// PostgreSQL 18: 20 of 4,240 instances failed, 8 starting at once on a fresh database). It ignores <c>23505</c>,
/// <c>42P07</c> and <c>42710</c> around the history table, not <c>42P06</c> from a history table in its own schema (not
/// seen in the same measurement, kept because nothing else stops it).
/// </para>
/// <para>
/// Another try is safe: whatever lost the race now exists, and the migrations themselves run under the lock, each in a
/// transaction with its history row, so the retry applies only what no one applied. An error a migration raises on its
/// own repeats on every try and still fails the start, as a restart would.
/// </para>
/// </remarks>
internal static class ConcurrentCreationRetry
{
    /// <summary>How many times an instance migrates while it keeps losing the race; the last failure throws.</summary>
    internal const int MaxAttempts = 5;

    /// <summary>The wait before the second try; it doubles before each further one (0.2, 0.4, 0.8, 1.6 s).</summary>
    internal static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>PostgreSQL's <c>SQLSTATE</c> for a database that already exists.</summary>
    internal const string DuplicateDatabase = "42P04";

    /// <summary>The <c>SQLSTATE</c> for a schema that already exists.</summary>
    internal const string DuplicateSchema = "42P06";

    /// <summary>The <c>SQLSTATE</c> for a table that already exists.</summary>
    internal const string DuplicateTable = "42P07";

    /// <summary>The <c>SQLSTATE</c> for another object (a type, a constraint) that already exists.</summary>
    internal const string DuplicateObject = "42710";

    /// <summary>
    /// The <c>SQLSTATE</c> for a unique violation: on PostgreSQL also what a concurrent <c>CREATE</c> gets from a system
    /// catalog's unique index (<c>pg_database_datname_index</c>, <c>pg_namespace_nspname_index</c>,
    /// <c>pg_type_typname_nsp_index</c>).
    /// </summary>
    internal const string UniqueViolation = "23505";

    /// <summary>
    /// Whether <paramref name="exception"/>, or the database exception it wraps (an execution strategy's
    /// <c>RetryLimitExceededException</c>, for one), says something already exists.
    /// </summary>
    /// <param name="exception">What migrating threw.</param>
    /// <param name="sqlState">The database's <c>SQLSTATE</c>, when it is one of these.</param>
    internal static bool IsConcurrentCreation(
        Exception exception,
        out string sqlState)
    {
        sqlState = FindSqlState(exception) ?? string.Empty;
        return sqlState is DuplicateDatabase
            or DuplicateSchema
            or DuplicateTable
            or DuplicateObject
            or UniqueViolation;
    }

    /// <summary>The wait after the failed try <paramref name="attempt"/> (1-based).</summary>
    internal static TimeSpan DelayAfter(int attempt)
        => FirstDelay * Math.Pow(2, attempt - 1);

    /// <summary>The <c>SQLSTATE</c> of the first database exception in <paramref name="exception"/>'s chain.</summary>
    private static string? FindSqlState(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException dbException)
                return dbException.SqlState;
        }

        return null;
    }
}
