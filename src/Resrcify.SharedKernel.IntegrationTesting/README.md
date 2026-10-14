# Resrcify.SharedKernel.IntegrationTesting

`Resrcify.SharedKernel.IntegrationTesting` holds the integration-test fixtures the services share: PostgreSQL and
RabbitMQ in Testcontainers, and a host fixture that runs a service's `Program` in-process on them.

## Table of Contents

- [What you get](#what-you-get)
- [Prerequisites](#prerequisites)
- [Install](#install)
- [Hosting a service: `ServiceHostFixture<TProgram>`](#hosting-a-service-servicehostfixturetprogram)
  - [Settings](#settings)
  - [A shared network](#a-shared-network)
  - [Starting more: `StartDependenciesAsync`](#starting-more-startdependenciesasync)
  - [Getting the started host ready: `OnHostStartedAsync`](#getting-the-started-host-ready-onhoststartedasync)
  - [A service without a database](#a-service-without-a-database)
  - [What a service changes](#what-a-service-changes)
  - [A Program with its own configuration builder](#a-program-with-its-own-configuration-builder)
- [Container fixtures](#container-fixtures)

## What you get

- `ServiceHostFixture<TProgram>` (`Factories/`): a `WebApplicationFactory<TProgram>` and xUnit fixture that starts
  PostgreSQL (unless the service has none), RabbitMQ when asked and whatever else the service starts in
  `StartDependenciesAsync`, hands the host the conventional settings through its configuration, starts the host and
  lets the service get it ready in `OnHostStartedAsync` (pause its jobs, seed data).
- `IntegrationFactoryBase<TProgram>` (`Factories/`): a `WebApplicationFactory` whose subclass supplies settings, for
  hosts that own no containers. The settings win over `appsettings*.json` and environment variables, with
  `WebApplication.CreateBuilder` and `Host.CreateDefaultBuilder` alike.
- `PostgresContainerFixture`, `RabbitMqContainerFixture` (`Fixtures/`): one container each, `postgres:18` and
  `rabbitmq:4.3.4-management` (the RabbitMQ Cluster Operator v2.23.0's default) by default (`RESRCIFY_TEST_POSTGRES_IMAGE` / `RESRCIFY_TEST_RABBITMQ_IMAGE` replace the
  image for every fixture), a readiness probe, `JoinNetwork(network, aliases)`, and for RabbitMQ
  `WaitForExchangePublish`.

## Prerequisites

- .NET 10 SDK and Docker on the machine running the tests.

## Install

```xml
<PackageReference Include="Resrcify.SharedKernel.IntegrationTesting" Version="<latest>" />
<!-- Its build targets find the service's content root for WebApplicationFactory. -->
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
```

## Hosting a service: `ServiceHostFixture<TProgram>`

```csharp
public sealed class ShardHostFixture
    : ServiceHostFixture<Program>
{
    protected override PostgresContainerFixture CreatePostgres() => new PostgresFixture();      // the service's own
    protected override RabbitMqContainerFixture? CreateRabbitMq() => new RabbitMqFixture();     // null: no broker
    protected override bool UsesNetwork => true;                                               // see below

    protected override void ConfigureSettings(IDictionary<string, string?> settings)
        => settings["Patreon:Token"] = "test";

    protected override void ConfigureTestServices(IServiceCollection services)
        => services.Replace(ServiceDescriptor.Singleton<IClock>(new FixedClock()));
}

[CollectionDefinition(Name)]
public sealed class ShardIntegrationCollection
    : ICollectionFixture<ShardHostFixture>
{
    public const string Name = nameof(ShardIntegrationCollection);
}
```

When it starts (`InitializeAsync`) it starts the containers (at once), adds the conventional settings, runs
`StartDependenciesAsync`, then `ConfigureSettings`, then builds and starts the host (a plain `WebApplicationFactory`
would wait for the first `CreateClient()` or `Services`), then runs `OnHostStartedAsync`. `Postgres`, `RabbitMq`,
`Network` and `Settings` are there for the tests. Disposing it stops the host, then what was handed to
`DisposeWithFixture` (last first), then the containers, then deletes the network. A start that fails (a container,
`StartDependenciesAsync`, `ConfigureSettings`, the host's own start, `OnHostStartedAsync`) tears down what it had
started the same way before it throws, so nothing is left running, and the failure is the fixture's, not the first
test's.

### Settings

| Key | Value |
|---|---|
| `Database:Host`, `Database:Port` | the container's host and mapped port |
| `Database:Database`, `Database:Username`, `Database:Password` | what the container was created with |
| `MessageBus:Host`, `MessageBus:Port` | the broker's host and mapped AMQP port (with RabbitMQ only) |
| `MessageBus:Username`, `MessageBus:Password` | the broker's credentials (with RabbitMQ only) |
| `Migrations:Run` | `true` (with PostgreSQL only) |

`ConfigureSettings` adds to them or changes them; the environment is `Development` (override `Environment`). They
reach the host with `UseSetting`, so `Program` sees them in `builder.Configuration` while it registers its services
(where `AddPostgresDbContext` binds `Database` and the services bind `MessageBus`), and as the last configuration
source, so they win over `appsettings*.json`, environment variables and the `Program`'s own sources on the generic
host (`Host.CreateDefaultBuilder` + `Startup`) too, where `UseSetting` alone lost to `appsettings.json`. They belong
to that host alone: nothing is
written to the process' environment, so two fixtures in two collections can run at once, each on its own containers.
The old fixtures' `Environment.SetEnvironmentVariable("Database__Host", ...)` were process-wide: they leaked into every
other collection and forced all tests into one.

The credentials come from the container's connection string, which Testcontainers doesn't quote: keep `;` out of a
test database's password.

### A shared network

With `UsesNetwork`, the fixture creates a network and puts PostgreSQL on it as `postgres` and RabbitMQ as `rabbitmq`
(`PostgresNetworkAlias`, `RabbitMqNetworkAlias`). A container a test starts on `Network` (another service, say)
reaches them at `postgres:5432` and `rabbitmq:5672`, while the in-process host keeps using the mapped ports.

### Starting more: `StartDependenciesAsync`

A service that needs another container (SwgohApi's image on the fixture's network, say, whose URL goes into the
settings) starts it in `StartDependenciesAsync`. It runs once PostgreSQL, RabbitMQ and the network are up, with the
conventional `Settings` in place, and before `ConfigureSettings` and the host:

```csharp
public sealed class SentinelHostFixture
    : ServiceHostFixture<Program>
{
    private SwgohApiPod _swgohApi = default!;

    protected override PostgresContainerFixture? CreatePostgres() => new PostgresFixture();
    protected override RabbitMqContainerFixture? CreateRabbitMq() => new RabbitMqFixture();
    protected override bool UsesNetwork => true;          // the pod reaches rabbitmq:5672

    protected override async Task StartDependenciesAsync(CancellationToken cancellationToken)
        => _swgohApi = DisposeWithFixture(await SwgohApiPod.StartAsync(RabbitMq!, Network!, cancellationToken));

    protected override void ConfigureSettings(IDictionary<string, string?> settings)
        => settings["SwgohApiService:Url"] = _swgohApi.Url.ToString();
}
```

`DisposeWithFixture(x)` has the fixture dispose `x` when it is torn down: after the host has stopped, before RabbitMQ,
PostgreSQL and the network, the last one handed over first. It takes an `IAsyncDisposable` (a Testcontainers
container, returned for chaining) or an `IAsyncLifetime` (a container fixture of this package), and may be called at
any time (a test that starts another pod later hands it over too). Hand a container over *before* starting it
(`var c = DisposeWithFixture(builder.Build()); await c.StartAsync(cancellationToken);`), so it is removed even if its
own start fails. The token is cancelled when the fixture is torn down.

### Getting the started host ready: `OnHostStartedAsync`

`OnHostStartedAsync(services, cancellationToken)` runs once, at the end of `InitializeAsync`: after the host has
started (its hosted services too, so Quartz's scheduler is running) and before the first test. `services` is the host's
root provider (`Services`); create a scope for scoped services. Sentinel's system tests pause the scheduled jobs, which
the tests then run on demand, and leave the outbox's on its schedule:

```csharp
public sealed class SentinelSystemHostFixture
    : ServiceHostFixture<Program>
{
    private static readonly string[] ScheduledJobs = ["UpdateGuildsJob", "UpdateTrackedPlayersJob", /* … */];

    protected override async Task OnHostStartedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken);
        foreach (var job in ScheduledJobs)
            await scheduler.PauseJob(new JobKey(job), cancellationToken);
        // or every job, the outbox's included: await scheduler.PauseAll(cancellationToken);

        await using var scope = services.CreateAsyncScope();
        await SeedAsync(scope.ServiceProvider, cancellationToken);   // data every test reads
    }
}
```

A failure in it tears down the host and everything started before it, like any failed start; it doesn't run when the
start failed earlier (the host's own start included). A job whose trigger fires as the scheduler starts may run once
before the hook pauses it: give such a job a start delay if a test can't tolerate that run.

There is no built-in "pause Quartz" switch: this package doesn't depend on Quartz (a service without jobs shouldn't
ship it), and which jobs to hold differs per service (Sentinel keeps the outbox running). The hook is the three lines
above.

### A service without a database

`CreatePostgres()` returning `null` starts no PostgreSQL and gives no `Database:*` or `Migrations:Run` settings
(Discord's bot has no database); reading `Postgres` then throws `InvalidOperationException`.

```csharp
protected override PostgresContainerFixture? CreatePostgres() => null;
```

### What a service changes

Before (~55 lines per service):

```csharp
public sealed class ShardHostFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public PostgresFixture Postgres { get; } = new();
    public RabbitMqFixture RabbitMq { get; } = new();

    public async Task InitializeAsync()
    {
        await Postgres.InitializeAsync();
        await RabbitMq.InitializeAsync();
        Environment.SetEnvironmentVariable("Database__Host", Postgres.Host);
        // ... nine more
    }

    async Task IAsyncLifetime.DisposeAsync() { ... }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}
```

After: the class above. The service's `PostgresFixture`/`RabbitMqFixture` stay as they are; a `RabbitMqFixture` that
built its own network can drop it for `UsesNetwork` (its alias is the same, `rabbitmq`). Tests reach the containers as
`host.Postgres` / `host.RabbitMq` (typed as the base fixtures; cast for the service's own members, or keep the
constants on the service's fixture classes).

### A Program with its own configuration builder

A `Program` that reads a configuration it builds itself never sees the fixture's settings:

```csharp
var configuration = new ConfigurationBuilder()      // Tournament's Program.cs
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();
builder.Services.AddPersistenceServices(configuration);
```

`builder.Configuration` already has the same JSON files, environment variables and command line: pass it instead
(`AddPersistenceServices(builder.Configuration)`) before moving such a service to this fixture.

## Container fixtures

Use them on their own (`IClassFixture<PostgresContainerFixture>`) when a test needs a database but no host;
`CreateIsolatedConnectionString()` gives each test class a database of its own. Subclass them to set the image,
database or credentials (`Configure`).
