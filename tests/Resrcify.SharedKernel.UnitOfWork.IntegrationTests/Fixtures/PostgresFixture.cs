using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Testcontainers.PostgreSql;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;

/// <summary>
/// The Postgres for this test project: the fixture's default image (the version production runs, or
/// <c>RESRCIFY_TEST_POSTGRES_IMAGE</c>) with this project's database and credentials.
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
            .WithDatabase("integration")
            .WithUsername("integration")
            .WithPassword("integration");
}
