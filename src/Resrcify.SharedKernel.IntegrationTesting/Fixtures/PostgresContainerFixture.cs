using Testcontainers.PostgreSql;

namespace Resrcify.SharedKernel.IntegrationTesting.Fixtures;

/// <summary>
/// Postgres-flavoured <see cref="ContainerFixture{TContainer}"/>. Subclasses
/// (or direct consumers) override <see cref="Configure"/> to set image,
/// database, credentials, and any other builder options. Defaults to whatever
/// <see cref="PostgreSqlBuilder"/> produces with no overrides — useful when
/// the consumer doesn't care about specific versions or naming.
/// </summary>
public class PostgresContainerFixture
    : ContainerFixture<PostgreSqlContainer>
{
    protected virtual PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
        => builder;

    protected override PostgreSqlContainer Build()
        => Configure(new PostgreSqlBuilder()).Build();
}
