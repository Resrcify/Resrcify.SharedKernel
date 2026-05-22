using System;
using System.Data.Common;
using System.Globalization;
using System.Threading.Tasks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Resrcify.SharedKernel.IntegrationTesting.Fixtures;

/// <summary>
/// Postgres-flavoured <see cref="ContainerFixture{TContainer}"/>. Subclasses
/// (or direct consumers) override <see cref="Configure"/> to set image,
/// database, credentials, and any other builder options. Defaults to whatever
/// <see cref="PostgreSqlBuilder"/> produces with no overrides — useful when
/// the consumer doesn't care about specific versions or naming.
///
/// Exposes <see cref="Host"/> and <see cref="Port"/> so consumers can wire
/// connection settings without poking at <see cref="ContainerFixture{TContainer}.Container"/>
/// directly. <see cref="Port"/> is the host-side mapped port for Postgres'
/// fixed in-container <see cref="ContainerPort"/> (5432).
/// </summary>
public class PostgresContainerFixture
    : ContainerFixture<PostgreSqlContainer>
{
    private const ushort ContainerPort = 5432;

    protected virtual PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
        => builder;

    protected override PostgreSqlContainer Build()
        => Configure(new PostgreSqlBuilder()).Build();

    public string Host
        => Container.Hostname;

    public string Port
        => Container.GetMappedPublicPort(ContainerPort).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The postgres image boots twice (init scripts on a temporary server, then the
    /// real server), so a passing wait strategy can still race the real listener on
    /// a cold or busy host. This probes a real <c>SELECT 1</c> until it succeeds.
    /// </summary>
    protected override async Task OnStartedAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(
                    Container.GetConnectionString());
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync();
                return;
            }
            catch (NpgsqlException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }
    }

    /// <summary>
    /// Returns a connection string for the container that targets a fresh,
    /// uniquely-named database. The database does not exist yet — EF Core creates
    /// it (and its schema) on the first <c>EnsureCreated</c> / <c>Migrate</c> call.
    /// Use one per test class (or per test) to get full isolation without sharing
    /// state or truncating tables between runs.
    /// </summary>
    public string CreateIsolatedConnectionString()
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = Container.GetConnectionString(),
        };
        builder["Database"] = $"test_{Guid.NewGuid():N}";
        return builder.ConnectionString;
    }
}
