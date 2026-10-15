using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.Abstractions.Health;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Health;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;

/// <summary>The PostgreSQL database's health check.</summary>
public static class PostgresHealthChecksBuilderExtensions
{
    /// <summary>
    /// Checks that <typeparamref name="TContext"/>'s database answers: it opens a connection with the connection string
    /// <c>AddPostgresDbContext&lt;TContext&gt;</c> built and runs <c>SELECT 1</c>, failing with
    /// <paramref name="failureStatus"/> (<see cref="HealthStatus.Unhealthy"/>) when it can't, within
    /// <paramref name="timeout"/> (the health check service's own when <see langword="null"/>). It replaces
    /// <c>AddNpgSql(provider =&gt; provider.GetPostgresConnectionString&lt;TContext&gt;(), ...)</c> and the
    /// <c>AspNetCore.HealthChecks.NpgSql</c> package. Needs <c>AddPostgresDbContext&lt;TContext&gt;</c>: without it the
    /// host fails to start, with a message saying so.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The check's name.</param>
    /// <param name="failureStatus">The status when the database doesn't answer (<see cref="HealthStatus.Unhealthy"/>).</param>
    /// <param name="tags">The check's tags: <see cref="HealthTags.Ready"/> when <see langword="null"/>; <c>[]</c> for none.</param>
    /// <param name="timeout">How long a check may take.</param>
    /// <example><c>services.AddHealthChecks().AddPostgres&lt;ShardDbContext&gt;();</c></example>
    public static IHealthChecksBuilder AddPostgres<TContext>(
        this IHealthChecksBuilder builder,
        string name = "postgres",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var missingContext = $"The PostgreSQL health check for {typeof(TContext).Name} reads the connection "
            + $"AddPostgresDbContext<{typeof(TContext).Name}>() builds: register the context with it.";
        // Fails the host's start (before any hosted service starts) instead of every check run.
        builder.Services
            .AddOptions<PostgresHealthCheckRequirement<TContext>>()
            .Validate<IServiceProviderIsService>(
                (_, services) => services.IsService(typeof(PostgresConnection<TContext>)),
                missingContext)
            .ValidateOnStart();

        return builder.Add(new HealthCheckRegistration(
            name,
            provider => new PostgresHealthCheck<TContext>(provider.GetService<PostgresConnection<TContext>>()
                ?? throw new InvalidOperationException(missingContext)),
            failureStatus,
            tags ?? [HealthTags.Ready],
            timeout));
    }
}

/// <summary>Marks that a PostgreSQL health check for <typeparamref name="TContext"/> is registered, for start-up validation.</summary>
internal sealed class PostgresHealthCheckRequirement<TContext>
    where TContext : DbContext
{
    /// <summary>The DbContext the check belongs to (the type parameter keys it in DI).</summary>
    public Type DbContextType { get; } = typeof(TContext);
}
