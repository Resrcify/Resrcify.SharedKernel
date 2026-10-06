using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Npgsql;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Testcontainers.PostgreSql;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Fixtures;

/// <summary>
/// The Postgres for this test project: the fixture's default image (the version production runs, or
/// <c>RESRCIFY_TEST_POSTGRES_IMAGE</c>) with this project's credentials.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit class fixtures must be public for cross-assembly fixture discovery.")]
public sealed class PostgresFixture
    : PostgresContainerFixture
{
    internal const string Username = "integration";
    internal const string Password = "integration";

    protected override PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
        => builder
            .WithDatabase("integration")
            .WithUsername(Username)
            .WithPassword(Password);

    /// <summary>The <c>Database</c> section for a fresh, uniquely named database in the container.</summary>
    internal Dictionary<string, string?> IsolatedDatabaseSettings()
    {
        var database = new NpgsqlConnectionStringBuilder(CreateIsolatedConnectionString()).Database;
        return new Dictionary<string, string?>
        {
            ["Database:Host"] = Host,
            ["Database:Port"] = Port,
            ["Database:Database"] = database,
            ["Database:Username"] = Username,
            ["Database:Password"] = Password,
        };
    }
}
