using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Health;

/// <summary>
/// Whether <typeparamref name="TContext"/>'s database answers: opens a connection with the connection string
/// <c>AddPostgresDbContext</c> built (from the pool) and runs <c>SELECT 1</c>.
/// </summary>
internal sealed class PostgresHealthCheck<TContext>(
    PostgresConnection<TContext> connection)
    : IHealthCheck
    where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await using var database = new NpgsqlConnection(connection.ConnectionString);
            await database.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", database);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"{typeof(TContext).Name}'s PostgreSQL database didn't answer: {exception.Message}",
                exception);
        }
    }
}
