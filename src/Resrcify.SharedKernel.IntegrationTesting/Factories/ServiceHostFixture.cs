using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
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
/// Hosts a service's <c>Program</c> in-process on its own PostgreSQL (unless it has no database) and, when asked for,
/// RabbitMQ and containers of its own, with the conventional settings handed to the host's configuration, not to
/// process-wide environment variables.
/// </summary>
/// <remarks>
/// <para>
/// When it starts (<see cref="InitializeAsync"/>) it starts the containers and gives the host: with PostgreSQL
/// <c>Database:Host</c>, <c>Database:Port</c>, <c>Database:Database</c>, <c>Database:Username</c>,
/// <c>Database:Password</c> and <c>Migrations:Run</c> = <c>true</c>; with RabbitMQ <c>MessageBus:Host</c>,
/// <c>MessageBus:Port</c>, <c>MessageBus:Username</c>, <c>MessageBus:Password</c> (what <c>AddPostgresDbContext</c> and
/// the bus' <c>UseRabbitMqFromConfiguration</c> read). Then it runs
/// <see cref="StartDependenciesAsync"/>, where a service starts what else it needs (another service's container on
/// <see cref="Network"/>, say), and <see cref="ConfigureSettings"/>, where it adds or changes settings (that
/// container's address). Then it builds and starts the host, and runs <see cref="OnHostStartedAsync"/>, where a service
/// gets the started host ready for its tests (pauses its scheduled jobs, seeds data). A service replaces services in
/// <see cref="ConfigureTestServices"/>. Two fixtures (two collections) each see their own containers: nothing is shared
/// through the process' environment.
/// </para>
/// <para>
/// The settings reach the host through <c>UseSetting</c>, so a <c>Program</c> sees them in <c>builder.Configuration</c>
/// already while it registers its services, and as the last configuration source, so they win over
/// <c>appsettings*.json</c>, environment variables and the <c>Program</c>'s own sources, with
/// <c>WebApplication.CreateBuilder</c> and with <c>Host.CreateDefaultBuilder</c> alike (see
/// <see cref="IntegrationFactoryBase{TProgram}"/>). A <c>Program</c> that builds its own
/// <c>new ConfigurationBuilder()...AddEnvironmentVariables()</c> and reads that instead never sees them: read the
/// host's configuration (<c>builder.Configuration</c>, which has the same JSON files, environment variables and
/// command line) to use this fixture.
/// </para>
/// <para>
/// Tearing down (<c>IAsyncLifetime.DisposeAsync</c>) stops the host, then disposes what was handed to
/// <see cref="DisposeWithFixture{TDependency}"/> (last first), then RabbitMQ, PostgreSQL and the network. A start that
/// fails, in a container, <see cref="StartDependenciesAsync"/>, <see cref="ConfigureSettings"/>, the host's own start or
/// <see cref="OnHostStartedAsync"/>, tears down what it had started the same way before it throws.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ShardHostFixture
///     : ServiceHostFixture&lt;Program&gt;
/// {
///     private SwgohApiContainer _swgohApi = default!;
///
///     protected override PostgresContainerFixture? CreatePostgres() => new PostgresFixture();   // null: no database
///     protected override RabbitMqContainerFixture? CreateRabbitMq() => new RabbitMqFixture();
///     protected override bool UsesNetwork => true;   // SwgohApi's container reaches rabbitmq:5672
///
///     protected override async Task StartDependenciesAsync(CancellationToken cancellationToken)
///     {
///         _swgohApi = DisposeWithFixture(new SwgohApiContainer(Network!, RabbitMqNetworkAlias));
///         await _swgohApi.StartAsync(cancellationToken);
///     }
///
///     protected override void ConfigureSettings(IDictionary&lt;string, string?&gt; settings)
///     {
///         settings["SwgohApiService:Url"] = _swgohApi.Url.ToString();
///         settings["Patreon:Token"] = "test";
///     }
///
///     protected override async Task OnHostStartedAsync(IServiceProvider services, CancellationToken cancellationToken)
///     {
///         var scheduler = await services.GetRequiredService&lt;ISchedulerFactory&gt;().GetScheduler(cancellationToken);
///         await scheduler.PauseAll(cancellationToken);   // the tests run the jobs themselves
///     }
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
    private readonly List<Func<Task>> _dependencies = [];
    private readonly Lock _dependenciesGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private PostgresContainerFixture? _postgres;
    private bool _networkCreated;
    private bool _tornDown;

    /// <summary>The service's database server. Set once the fixture has started.</summary>
    /// <exception cref="InvalidOperationException">
    /// The fixture hasn't started, or the service has no database (<see cref="CreatePostgres"/> returned
    /// <see langword="null"/>).
    /// </exception>
    public PostgresContainerFixture Postgres
        => _postgres ?? throw new InvalidOperationException(
            "This fixture has no PostgreSQL: it hasn't started, or CreatePostgres returned null.");

    /// <summary>The service's broker, when <see cref="CreateRabbitMq"/> makes one.</summary>
    public RabbitMqContainerFixture? RabbitMq { get; private set; }

    /// <summary>
    /// The network the containers are on, as <see cref="PostgresNetworkAlias"/> and <see cref="RabbitMqNetworkAlias"/>,
    /// when <see cref="UsesNetwork"/>: other containers (those of <see cref="StartDependenciesAsync"/>, or a test's)
    /// started on it reach them by those names.
    /// </summary>
    public INetwork? Network { get; private set; }

    /// <summary>The settings the host is given (configuration keys, <c>:</c>-separated).</summary>
    public IReadOnlyDictionary<string, string?> Settings
        => _settings;

    /// <summary>Puts the containers on a network of their own (<see cref="Network"/>). Off by default.</summary>
    protected virtual bool UsesNetwork
        => false;

    /// <summary>
    /// The PostgreSQL fixture: override to return the service's own (image, database, credentials), or
    /// <see langword="null"/> for a service without a database (no container, no <c>Database:*</c> or
    /// <c>Migrations:Run</c> settings).
    /// </summary>
    protected virtual PostgresContainerFixture? CreatePostgres()
        => new();

    /// <summary>
    /// The RabbitMQ fixture, or <see langword="null"/> (the default) for a service without a message bus. Override to
    /// return one (<c>new RabbitMqContainerFixture()</c>, or the service's own).
    /// </summary>
    protected virtual RabbitMqContainerFixture? CreateRabbitMq()
        => null;

    /// <summary>
    /// Starts what else the service needs, e.g. another service's container on <see cref="Network"/>. Runs once
    /// PostgreSQL, RabbitMQ and the network are up (<see cref="Postgres"/>, <see cref="RabbitMq"/>,
    /// <see cref="Network"/>, and <see cref="Settings"/> with the conventional settings), before
    /// <see cref="ConfigureSettings"/> and before the host is built. Nothing by default.
    /// </summary>
    /// <remarks>
    /// Hand what it starts to <see cref="DisposeWithFixture{TDependency}"/> <em>before</em> starting it
    /// (<c>var api = DisposeWithFixture(builder.Build()); await api.StartAsync(cancellationToken);</c>), so that it is
    /// disposed if its own start, or anything after it, fails.
    /// </remarks>
    /// <param name="cancellationToken">Cancelled when the fixture is torn down.</param>
    protected virtual Task StartDependenciesAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Adds or changes settings; <paramref name="settings"/> already has the conventional ones. Runs once the
    /// containers have started, after <see cref="StartDependenciesAsync"/>.
    /// </summary>
    protected virtual void ConfigureSettings(IDictionary<string, string?> settings)
    {
        // Nothing to add by default.
    }

    /// <summary>
    /// Gets the started host ready for the tests, e.g. pauses its scheduled jobs (Quartz's <c>IScheduler.PauseAll()</c>,
    /// so the tests run them on demand) or seeds data. Runs once, at the end of <see cref="InitializeAsync"/>, after
    /// the host has started (its hosted services included) and before the first test. Nothing by default.
    /// </summary>
    /// <remarks>
    /// <paramref name="services"/> is the host's root provider: create a scope for scoped services (a <c>DbContext</c>).
    /// A failure tears down the host and everything started before it, as any failed start does. A job whose trigger
    /// fires as the scheduler starts may run once before this pauses it.
    /// </remarks>
    /// <param name="services">The started host's services (<c>Services</c>).</param>
    /// <param name="cancellationToken">Cancelled when the fixture is torn down.</param>
    protected virtual Task OnHostStartedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>Replaces the host's services (<c>ConfigureTestServices</c>): runs after the service's own registrations.</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        // Nothing to replace by default.
    }

    /// <summary>
    /// Has the fixture dispose <paramref name="dependency"/> when it is torn down: after the host has stopped, before
    /// RabbitMQ, PostgreSQL and the network, the last one handed over first. For what
    /// <see cref="StartDependenciesAsync"/> (or a test) starts; it may be called at any time, from any thread.
    /// </summary>
    /// <returns><paramref name="dependency"/>.</returns>
    protected TDependency DisposeWithFixture<TDependency>(TDependency dependency)
        where TDependency : IAsyncDisposable
    {
        ArgumentNullException.ThrowIfNull(dependency);
        AddDependency(async () => await dependency.DisposeAsync());
        return dependency;
    }

    /// <summary>
    /// Has the fixture dispose <paramref name="fixture"/> (<c>IAsyncLifetime.DisposeAsync</c>) when it is torn down, as
    /// <see cref="DisposeWithFixture{TDependency}"/> does: for a container fixture (a <see cref="ContainerFixture{TContainer}"/>,
    /// such as another <see cref="RabbitMqContainerFixture"/>) that <see cref="StartDependenciesAsync"/> starts with
    /// <c>InitializeAsync</c>.
    /// </summary>
    protected void DisposeWithFixture(IAsyncLifetime fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        AddDependency(fixture.DisposeAsync);
    }

    /// <summary>
    /// Starts the containers, <see cref="StartDependenciesAsync"/>, the settings, then the host, and runs
    /// <see cref="OnHostStartedAsync"/>. On a failure it tears down what it had started, then throws.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            await StartAsync();
        }
        catch (Exception failure)
        {
            var teardownFailures = await TearDownAsync();
            if (teardownFailures.Count > 0)
                throw new AggregateException(
                    "Starting the service host failed, and so did tearing down what it had started.",
                    (IEnumerable<Exception>)[failure, .. teardownFailures]);
            throw;
        }
    }

    // Tears down once: after a failed start has torn down, xUnit's own call finds nothing left to do.
    async Task IAsyncLifetime.DisposeAsync()
    {
        var failures = await TearDownAsync();
        if (failures.Count == 1)
            ExceptionDispatchInfo.Throw(failures[0]);
        if (failures.Count > 1)
            throw new AggregateException("Tearing the service host down failed.", failures);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _lifetime.Dispose();
        base.Dispose(disposing);
    }

    protected sealed override IDictionary<string, string?> EnvOverrides()
        => _settings;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(ConfigureTestServices);
    }

    private async Task StartAsync()
    {
        if (UsesNetwork)
            Network = new NetworkBuilder().Build();

        _postgres = CreatePostgres();
        RabbitMq = CreateRabbitMq();
        if (Network is { } network)
        {
            await network.CreateAsync(_lifetime.Token);
            _networkCreated = true;
            _postgres?.JoinNetwork(network, PostgresNetworkAlias);
            RabbitMq?.JoinNetwork(network, RabbitMqNetworkAlias);
        }

        await Task.WhenAll(
            _postgres?.InitializeAsync() ?? Task.CompletedTask,
            RabbitMq?.InitializeAsync() ?? Task.CompletedTask);

        AddDatabaseSettings();
        AddMessageBusSettings();

        await StartDependenciesAsync(_lifetime.Token);
        ConfigureSettings(_settings);

        // WebApplicationFactory builds and starts the host on first use (Services, CreateClient): start it here, so a
        // host that can't start fails the fixture (and tears it down) rather than the first test.
        var services = Services;
        await OnHostStartedAsync(services, _lifetime.Token);
    }

    // Every step runs whatever the one before did (a container that failed to build, a host that failed to stop), so
    // nothing started is left behind; their failures are returned together.
    private async Task<List<Exception>> TearDownAsync()
    {
        var failures = new List<Exception>();
        if (_tornDown)
            return failures;
        _tornDown = true;

        await CancelLifetimeAsync();
        await RunAsync(async () => await DisposeAsync(), failures);
        foreach (var dispose in TakeDependencies())
            await RunAsync(dispose, failures);
        if (RabbitMq is not null)
            await RunAsync(RabbitMq.DisposeAsync, failures);
        if (_postgres is not null)
            await RunAsync(_postgres.DisposeAsync, failures);
        if (Network is not null && _networkCreated)
            await RunAsync(() => Network.DeleteAsync(CancellationToken.None), failures);

        return failures;
    }

    private async Task CancelLifetimeAsync()
    {
        try
        {
            await _lifetime.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // The factory was disposed directly before: whatever ran on the token has stopped with the host.
        }
    }

    private void AddDependency(Func<Task> dispose)
    {
        lock (_dependenciesGate)
            _dependencies.Add(dispose);
    }

    // The last handed over first, as each may depend on those before it.
    private List<Func<Task>> TakeDependencies()
    {
        lock (_dependenciesGate)
        {
            var dependencies = new List<Func<Task>>(_dependencies);
            _dependencies.Clear();
            dependencies.Reverse();
            return dependencies;
        }
    }

    private static async Task RunAsync(Func<Task> step, List<Exception> failures)
    {
        try
        {
            await step();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }

    private void AddDatabaseSettings()
    {
        if (_postgres is null)
            return;

        var connection = new NpgsqlConnectionStringBuilder(_postgres.Container.GetConnectionString());
        _settings["Database:Host"] = _postgres.Host;
        _settings["Database:Port"] = _postgres.Port;
        _settings["Database:Database"] = connection.Database;
        _settings["Database:Username"] = connection.Username;
        _settings["Database:Password"] = connection.Password;
        _settings["Migrations:Run"] = "true";
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
