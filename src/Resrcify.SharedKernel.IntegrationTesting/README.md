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
  - [What a service changes](#what-a-service-changes)
  - [A Program with its own configuration builder](#a-program-with-its-own-configuration-builder)
- [Container fixtures](#container-fixtures)

## What you get

- `ServiceHostFixture<TProgram>` (`Factories/`): a `WebApplicationFactory<TProgram>` and xUnit fixture that starts
  PostgreSQL (and RabbitMQ when asked), and hands the host the conventional settings through its configuration.
- `IntegrationFactoryBase<TProgram>` (`Factories/`): a `WebApplicationFactory` whose subclass supplies settings
  (`UseSetting`), for hosts that own no containers.
- `PostgresContainerFixture`, `RabbitMqContainerFixture` (`Fixtures/`): one container each, `postgres:18` and
  `rabbitmq:4.3-management` by default (`RESRCIFY_TEST_POSTGRES_IMAGE` / `RESRCIFY_TEST_RABBITMQ_IMAGE` replace the
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

When it starts (`InitializeAsync`) it starts the containers (at once), then builds the settings. The host itself is
built on first use (`CreateClient()`, `Services`), as with any `WebApplicationFactory`. `Postgres`, `RabbitMq`,
`Network` and `Settings` are there for the tests. Disposing it stops the host, then the containers, then deletes the
network.

### Settings

| Key | Value |
|---|---|
| `Database:Host`, `Database:Port` | the container's host and mapped port |
| `Database:Database`, `Database:Username`, `Database:Password` | what the container was created with |
| `MessageBus:Host`, `MessageBus:Port` | the broker's host and mapped AMQP port (with RabbitMQ only) |
| `MessageBus:Username`, `MessageBus:Password` | the broker's credentials (with RabbitMQ only) |
| `Migrations:Run` | `true` |

`ConfigureSettings` adds to them or changes them; the environment is `Development` (override `Environment`). They
reach the host with `UseSetting`, so `Program` sees them in `builder.Configuration` while it registers its services
(where `AddPostgresDbContext` binds `Database` and the services bind `MessageBus`), and they belong to that host alone: nothing is
written to the process' environment, so two fixtures in two collections can run at once, each on its own containers.
The old fixtures' `Environment.SetEnvironmentVariable("Database__Host", ...)` were process-wide: they leaked into every
other collection and forced all tests into one.

The credentials come from the container's connection string, which Testcontainers doesn't quote: keep `;` out of a
test database's password.

### A shared network

With `UsesNetwork`, the fixture creates a network and puts PostgreSQL on it as `postgres` and RabbitMQ as `rabbitmq`
(`PostgresNetworkAlias`, `RabbitMqNetworkAlias`). A container a test starts on `Network` (another service, say)
reaches them at `postgres:5432` and `rabbitmq:5672`, while the in-process host keeps using the mapped ports.

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
