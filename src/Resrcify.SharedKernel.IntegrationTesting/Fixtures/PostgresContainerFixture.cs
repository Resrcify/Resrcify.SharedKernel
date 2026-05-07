using System.Globalization;
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
}
