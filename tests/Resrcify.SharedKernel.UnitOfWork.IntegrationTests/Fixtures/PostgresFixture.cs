using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Testcontainers.PostgreSql;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;

/// <summary>
/// Pins a specific Postgres image for this test project. Override exists
/// to demonstrate the per-consumer customisation point — most consumers
/// will pin a major version they care about.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit class fixtures must be public for cross-assembly fixture discovery.")]
public sealed class PostgresFixture
    : PostgresContainerFixture
{
    protected override PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
        => builder
            .WithImage("postgres:16")
            .WithDatabase("integration")
            .WithUsername("integration")
            .WithPassword("integration");
}
