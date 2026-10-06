using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;

/// <summary>
/// How <c>AddPostgresDbContext</c> (and <c>PostgresDesignTimeFactory</c>) set a DbContext up. Every option is opt-in;
/// without any, the context connects with the <c>Database</c> section's settings and has the auditable and
/// soft-delete interceptors.
/// </summary>
/// <remarks>
/// Share the part <c>dotnet ef</c> needs (<see cref="ConfigureNpgsql"/>, <see cref="ConfigureDbContext"/>,
/// <see cref="FromSection"/>) between the service's registration and its design-time factory, e.g. in a static method
/// both call; the outbox, the wake-up and the retries matter only at run time.
/// </remarks>
public sealed class PostgresDbContextBuilder
{
    private readonly List<Action<NpgsqlConnectionStringBuilder>> _connection = [];
    private readonly List<Action<NpgsqlDbContextOptionsBuilder>> _npgsql = [];
    private readonly List<Action<DbContextOptionsBuilder>> _dbContext = [];

    /// <summary>The configuration section the connection settings are read from; <c>Database</c> by default.</summary>
    public string SectionName { get; private set; } = PostgresOptions.SectionName;

    /// <summary>The outbox's write side, when <see cref="WithOutbox"/> was called.</summary>
    internal PostgresOutboxOptions? Outbox { get; private set; }

    /// <summary>The outbox wake-up, when <see cref="WithOutboxWakeUp"/> was called.</summary>
    internal OutboxWakeUpOptions? WakeUp { get; private set; }

    /// <summary>The retries, when <see cref="RetryOnFailure"/> was called.</summary>
    internal (int MaxRetryCount, TimeSpan MaxRetryDelay)? Retry { get; private set; }

    /// <summary>Reads the connection settings from <paramref name="sectionName"/> instead of <c>Database</c>.</summary>
    public PostgresDbContextBuilder FromSection(string sectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        SectionName = sectionName;
        return this;
    }

    /// <summary>
    /// Writes the domain events of saved aggregates to the outbox (<c>InsertOutboxMessagesInterceptor</c>, built by the
    /// container with its <see cref="TimeProvider"/>). The serializer is the registered <c>IOutboxSerializer</c>
    /// (<see cref="PostgresOutboxOptions.Serializer"/>, or <c>SystemTextJsonOutboxSerializer</c>), the one the outbox job
    /// reads with. Processing stays <c>AddOutboxProcessing</c> / <c>AddProcessOutboxMessagesJob</c>.
    /// </summary>
    public PostgresDbContextBuilder WithOutbox(Action<PostgresOutboxOptions>? configure = null)
    {
        var options = new PostgresOutboxOptions();
        configure?.Invoke(options);
        Outbox = options;
        return this;
    }

    /// <summary>
    /// Wakes the outbox as soon as a save commits outbox messages, instead of at its next poll: the save sends a
    /// PostgreSQL <c>NOTIFY</c> (delivered on commit only), and a listener on one dedicated connection runs the outbox
    /// job and lets the lanes poll at once. The polling stays as the safety net. Needs <see cref="WithOutbox"/>.
    /// </summary>
    public PostgresDbContextBuilder WithOutboxWakeUp(Action<OutboxWakeUpOptions>? configure = null)
    {
        var options = new OutboxWakeUpOptions();
        configure?.Invoke(options);
        options.Validate();
        WakeUp = options;
        return this;
    }

    /// <summary>
    /// Retries a command or save that failed transiently (a dropped connection, a failover, a serialization failure or
    /// a deadlock), with Npgsql's retrying execution strategy (<c>EnableRetryOnFailure</c>), up to
    /// <paramref name="maxRetryCount"/> times, waiting up to <paramref name="maxRetryDelay"/> (30 s by default).
    /// </summary>
    /// <remarks>
    /// The unit of work works with it: <c>CompleteAsync</c> and <c>ExecuteInTransactionAsync</c> run under the strategy
    /// (a retry runs the whole transaction again). EF Core refuses a transaction begun by hand under it: replace
    /// <c>BeginTransactionAsync</c> / <c>CommitTransactionAsync</c> with <c>ExecuteInTransactionAsync</c> first, and save
    /// through <c>IUnitOfWork</c> rather than <c>DbContext.SaveChangesAsync</c> when the outbox inserts with
    /// <see cref="PostgresOutboxOptions.OnConflictDoNothing"/> (that insert opens a transaction of its own).
    /// </remarks>
    public PostgresDbContextBuilder RetryOnFailure(
        int maxRetryCount = 6,
        TimeSpan? maxRetryDelay = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetryCount);
        if (maxRetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxRetryDelay), maxRetryDelay, "The delay must be positive.");
        Retry = (maxRetryCount, maxRetryDelay ?? TimeSpan.FromSeconds(30));
        return this;
    }

    /// <summary>Changes the connection string built from the section (pooling, timeouts, an application name, ...).</summary>
    public PostgresDbContextBuilder ConfigureConnection(Action<NpgsqlConnectionStringBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _connection.Add(configure);
        return this;
    }

    /// <summary>The service's own Npgsql options (<c>UseNpgsql(..., npgsql =&gt; ...)</c>): migrations table, enums, ...</summary>
    public PostgresDbContextBuilder ConfigureNpgsql(Action<NpgsqlDbContextOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _npgsql.Add(configure);
        return this;
    }

    /// <summary>The service's own EF Core options (logging, detailed errors, more interceptors, ...).</summary>
    public PostgresDbContextBuilder ConfigureDbContext(Action<DbContextOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _dbContext.Add(configure);
        return this;
    }

    /// <summary>The connection string for <paramref name="options"/>, with <see cref="ConfigureConnection"/> applied.</summary>
    internal string ConnectionString(PostgresOptions options)
    {
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = options.Host,
            Port = options.Port,
            Database = options.Database,
            Username = options.Username,
            Password = options.Password,
        };
        if (options.CommandTimeoutInSeconds is { } commandTimeout)
            connection.CommandTimeout = commandTimeout;
        foreach (var configure in _connection)
            configure(connection);
        return connection.ConnectionString;
    }

    /// <summary>Points <paramref name="options"/> at <paramref name="connectionString"/> with the retries and the hooks.</summary>
    internal void Apply(
        DbContextOptionsBuilder options,
        string connectionString)
    {
        options.UseNpgsql(connectionString, npgsql =>
        {
            if (Retry is { } retry)
                npgsql.EnableRetryOnFailure(retry.MaxRetryCount, retry.MaxRetryDelay, errorCodesToAdd: null);
            foreach (var configure in _npgsql)
                configure(npgsql);
        });
        foreach (var configure in _dbContext)
            configure(options);
    }
}
