using System;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;

/// <summary>How <typeparamref name="TContext"/> is set up (what <c>AddPostgresDbContext</c> was given).</summary>
internal sealed record PostgresDbContextSettings<TContext>(PostgresDbContextBuilder Builder)
    where TContext : DbContext
{
    /// <summary>The name of <typeparamref name="TContext"/>'s <see cref="PostgresOptions"/> (named options, one per context).</summary>
    public static string OptionsName => typeof(TContext).FullName ?? typeof(TContext).Name;
}

/// <summary>
/// <typeparamref name="TContext"/>'s connection string, built once from its validated <see cref="PostgresOptions"/> and
/// the builder's <c>ConfigureConnection</c> hooks. Shared by the DbContext, the outbox listener and
/// <c>GetPostgresConnectionString</c>.
/// </summary>
internal sealed class PostgresConnection<TContext>
    where TContext : DbContext
{
    private readonly Lazy<string> _connectionString;

    public PostgresConnection(
        IOptionsMonitor<PostgresOptions> options,
        PostgresDbContextSettings<TContext> settings)
        => _connectionString = new(
            () => settings.Builder.ConnectionString(options.Get(PostgresDbContextSettings<TContext>.OptionsName)),
            LazyThreadSafetyMode.ExecutionAndPublication);

    public string ConnectionString => _connectionString.Value;
}
