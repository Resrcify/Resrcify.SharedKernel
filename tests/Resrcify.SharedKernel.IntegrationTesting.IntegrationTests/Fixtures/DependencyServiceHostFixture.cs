using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Resrcify.SharedKernel.IntegrationTesting.Factories;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Testcontainers.PostgreSql;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;

/// <summary>
/// A service without a database of the fixture's, which starts a container of its own on the fixture's network in
/// <c>StartDependenciesAsync</c> (a PostgreSQL standing in for another service) and points its settings at it in
/// <c>ConfigureSettings</c>; its <c>appsettings.json</c> sets a setting the fixture gives too.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit class fixtures must be public")]
public sealed class DependencyServiceHostFixture
    : ServiceHostFixture<Program>
{
    public const string DependencyAlias = "dependency";
    public const string DependencyDatabase = "DependencyDb";
    public const string SampleValue = "from the fixture";

    /// <summary>The container started in <c>StartDependenciesAsync</c>.</summary>
    public DependencyPostgres Dependency { get; private set; } = default!;

    /// <summary>Whether the network was up when <c>StartDependenciesAsync</c> ran.</summary>
    public bool NetworkWasUpInTheHook { get; private set; }

    protected override bool UsesNetwork
        => true;

    protected override PostgresContainerFixture? CreatePostgres()
        => null;

    protected override async Task StartDependenciesAsync(CancellationToken cancellationToken)
    {
        NetworkWasUpInTheHook = Network is not null;
        Dependency = new DependencyPostgres();
        Dependency.JoinNetwork(Network!, DependencyAlias);
        DisposeWithFixture(Dependency);
        await Dependency.InitializeAsync();
    }

    protected override void ConfigureSettings(IDictionary<string, string?> settings)
    {
        var connection = new NpgsqlConnectionStringBuilder(Dependency.Container.GetConnectionString());
        settings["Database:Host"] = Dependency.Host;
        settings["Database:Port"] = Dependency.Port;
        settings["Database:Database"] = connection.Database;
        settings["Database:Username"] = connection.Username;
        settings["Database:Password"] = connection.Password;
        settings["Sample:Value"] = SampleValue;
    }

    /// <summary>The service's other dependency, with a database name of its own.</summary>
    public sealed class DependencyPostgres
        : PostgresContainerFixture
    {
        protected override PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
            => builder.WithDatabase(DependencyDatabase);
    }
}
