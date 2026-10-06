using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.Factories;

/// <summary>
/// Hosts a service's <c>Program</c> in-process on its own PostgreSQL (and, when asked for, RabbitMQ), with the
/// conventional settings handed to the host's configuration, not to process-wide environment variables.
/// </summary>
/// <remarks>
/// <para>
/// When it starts (<see cref="InitializeAsync"/>) it starts the containers, then gives the host:
/// <c>Database:Host</c>, <c>Database:Port</c>, <c>Database:Database</c>, <c>Database:Username</c>,
/// <c>Database:Password</c>; with RabbitMQ <c>MessageBus:Host</c>, <c>MessageBus:Port</c>, <c>MessageBus:Username</c>,
/// <c>MessageBus:Password</c>; and <c>Migrations:Run</c> = <c>true</c>. A service adds or changes settings in
/// <see cref="ConfigureSettings"/> and replaces services in <see cref="ConfigureTestServices"/>. Two fixtures (two
/// collections) each see their own containers: nothing is shared through the process' environment.
/// </para>
/// <para>
/// The settings reach the host through <c>UseSetting</c>, so a <c>Program</c> sees them in <c>builder.Configuration</c>
/// already while it registers its services. A <c>Program</c> that builds its own
/// <c>new ConfigurationBuilder()...AddEnvironmentVariables()</c> and reads that instead never sees them: read the
/// host's configuration (<c>builder.Configuration</c>, which has the same JSON files, environment variables and
/// command line) to use this fixture.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ShardHostFixture
///     : ServiceHostFixture&lt;Program&gt;
/// {
///     protected override PostgresContainerFixture CreatePostgres() => new PostgresFixture();
///     protected override RabbitMqContainerFixture? CreateRabbitMq() => new RabbitMqFixture();
///     protected override bool UsesNetwork => true;   // SwgohApi containers reach rabbitmq:5672
///     protected override void ConfigureSettings(IDictionary&lt;string, string?&gt; settings)
///         => settings["Patreon:Token"] = "test";
/// }
/// </code>
/// </example>
public abstract class ServiceHostFixture<TProgram>
    : IntegrationFactoryBase<TProgram>,
    IAsyncLifetime
    where TProgram : class
{
    /// <summary>PostgreSQL's host name on <see cref="Network"/>.</summary>
    public const string PostgresNetworkAlias = "postgres";

    /// <summary>RabbitMQ's host name on <see cref="Network"/>.</summary>
    public const string RabbitMqNetworkAlias = "rabbitmq";

    private readonly Dictionary<string, string?> _settings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The service's database server. Set once the fixture has started.</summary>
    public PostgresContainerFixture Postgres { get; private set; } = default!;

    /// <summary>The service's broker, when <see cref="CreateRabbitMq"/> makes one.</summary>
    public RabbitMqContainerFixture? RabbitMq { get; private set; }

    /// <summary>
    /// The network the containers are on, as <see cref="PostgresNetworkAlias"/> and <see cref="RabbitMqNetworkAlias"/>,
    /// when <see cref="UsesNetwork"/>: other containers a test starts on it reach them by those names.
    /// </summary>
    public INetwork? Network { get; private set; }

    /// <summary>The settings the host is given (configuration keys, <c>:</c>-separated).</summary>
    public IReadOnlyDictionary<string, string?> Settings
        => _settings;

    /// <summary>Puts the containers on a network of their own (<see cref="Network"/>). Off by default.</summary>
    protected virtual bool UsesNetwork
        => false;

    /// <summary>The PostgreSQL fixture: override to return the service's own (image, database, credentials).</summary>
    protected virtual PostgresContainerFixture CreatePostgres()
        => new();

    /// <summary>
    /// The RabbitMQ fixture, or <see langword="null"/> (the default) for a service without a message bus. Override to
    /// return one (<c>new RabbitMqContainerFixture()</c>, or the service's own).
    /// </summary>
    protected virtual RabbitMqContainerFixture? CreateRabbitMq()
        => null;

    /// <summary>
    /// Adds or changes settings; <paramref name="settings"/> already has the conventional ones. Runs once the
    /// containers have started.
    /// </summary>
    protected virtual void ConfigureSettings(IDictionary<string, string?> settings)
    {
        // Nothing to add by default.
    }

    /// <summary>Replaces the host's services (<c>ConfigureTestServices</c>): runs after the service's own registrations.</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        // Nothing to replace by default.
    }

    public async Task InitializeAsync()
    {
        if (UsesNetwork)
            Network = new NetworkBuilder().Build();

        Postgres = CreatePostgres();
        RabbitMq = CreateRabbitMq();
        if (Network is { } network)
        {
            await network.CreateAsync();
            Postgres.JoinNetwork(network, PostgresNetworkAlias);
            RabbitMq?.JoinNetwork(network, RabbitMqNetworkAlias);
        }

        await Task.WhenAll(
            Postgres.InitializeAsync(),
            RabbitMq?.InitializeAsync() ?? Task.CompletedTask);

        AddDatabaseSettings();
        AddMessageBusSettings();
        _settings["Migrations:Run"] = "true";
        ConfigureSettings(_settings);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        if (RabbitMq is not null)
            await RabbitMq.DisposeAsync();
        if (Postgres is not null)
            await Postgres.DisposeAsync();
        if (Network is not null)
            await Network.DeleteAsync();
    }

    protected sealed override IDictionary<string, string?> EnvOverrides()
        => _settings;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(ConfigureTestServices);
    }

    private void AddDatabaseSettings()
    {
        var connection = new NpgsqlConnectionStringBuilder(Postgres.Container.GetConnectionString());
        _settings["Database:Host"] = Postgres.Host;
        _settings["Database:Port"] = Postgres.Port;
        _settings["Database:Database"] = connection.Database;
        _settings["Database:Username"] = connection.Username;
        _settings["Database:Password"] = connection.Password;
    }

    private void AddMessageBusSettings()
    {
        if (RabbitMq is null)
            return;

        var userInfo = new Uri(RabbitMq.Container.GetConnectionString()).UserInfo.Split(':', 2);
        _settings["MessageBus:Host"] = RabbitMq.Host;
        _settings["MessageBus:Port"] = RabbitMq.AmqpPort;
        _settings["MessageBus:Username"] = Uri.UnescapeDataString(userInfo[0]);
        _settings["MessageBus:Password"] = userInfo.Length > 1
            ? Uri.UnescapeDataString(userInfo[1])
            : string.Empty;
    }
}
