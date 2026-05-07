# Integration testing pattern

A proposal for `Resrcify.SharedKernel.IntegrationTesting` — a small package that ships reusable fixtures so each downstream service can stand up a real Postgres + RabbitMQ + in-process host with a few lines, and assert behaviour that unit tests can't reach (transaction interleaving, exchange routing, migration application, end-to-end outbox dispatch).

## Goals

- Catch dispatch / consumer / endpoint / migration regressions automatically via `dotnet test`.
- Each test fixture spins up only the dependencies it needs — a Sandbox test doesn't pay for RabbitMQ, a Discord test doesn't pay for Postgres.
- Container images are **per-consumer overridable** so different services can pin different Postgres / RabbitMQ versions.
- No machine-wide config mutations. No host port allocation. No leaked containers.

## Why not unit tests for everything

Unit tests with in-memory fakes can't catch: silent zero-handler dispatch through real DI registration (the SharedKernel.Messaging 1.15.0 outbox bug), Npgsql-specific type mapping issues, migration ordering against a fresh schema, MassTransit exchange topology, transactional outbox interceptor timing under real `SaveChangesAsync`. Integration tests close those gaps without running the full k8s stack.

## Tooling

| Component | Package | Role |
|---|---|---|
| DB containers | `Testcontainers.PostgreSql` | One Postgres per fixture, dynamic port, auto-cleanup |
| MQ containers | `Testcontainers.RabbitMq` | Real RabbitMQ for outbox / MassTransit assertions |
| In-process host | `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`) | SUT runs in the test process |
| HTTP stubs | `WireMock.Net` | Replace cross-service HTTP dependencies |
| Test framework | `xUnit` + `Shouldly` | Matches existing convention |

## Reusable base in `Resrcify.SharedKernel.IntegrationTesting`

The package ships:

1. **`ContainerFixture<TContainer, TBuilder>`** — generic container lifecycle base. Postgres and RabbitMQ both wrap a Testcontainers container with the same start/stop pattern, so the lifecycle code lives once.
2. **`PostgresContainerFixture`** / **`RabbitMqContainerFixture`** — thin tech-specific subclasses that bind the type parameters and add tech-specific helpers (e.g. RMQ management API helpers). Distinct types are required so xUnit's `ICollectionFixture<T>` can inject "the Postgres" and "the RabbitMQ" as separate dependencies.
3. **`IntegrationFactoryBase<TProgram>`** — `WebApplicationFactory<TProgram>` wrapper. Hosts the SUT in-process; structurally different from the container fixtures (no docker lifecycle, just config swap).

The container base owns **lifecycle only**. Image, credentials, database name, network options, init scripts, and anything else are configured by the consumer through the underlying `Testcontainers` builder. The base holds no opinion on naming, versioning, or auth defaults.

### `ContainerFixture<TContainer, TBuilder>`

```csharp
namespace Resrcify.SharedKernel.IntegrationTesting.Fixtures;

public abstract class ContainerFixture<TContainer, TBuilder> : IAsyncLifetime
    where TContainer : IContainer
    where TBuilder : IContainerBuilder<TBuilder, TContainer>, new()
{
    public TContainer Container { get; private set; } = default!;

    /// Override to fully configure the container.
    /// Consumers set image, credentials, options through the Testcontainers builder.
    protected virtual TBuilder Configure(TBuilder builder) => builder;

    public async Task InitializeAsync()
    {
        Container = Configure(new TBuilder()).Build();
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
        => await Container.DisposeAsync();
}
```

### `PostgresContainerFixture` / `RabbitMqContainerFixture`

```csharp
public class PostgresContainerFixture
    : ContainerFixture<PostgreSqlContainer, PostgreSqlBuilder>;

public class RabbitMqContainerFixture
    : ContainerFixture<RabbitMqContainer, RabbitMqBuilder>
{
    public string ManagementUrl
        => $"http://{Container.Hostname}:{Container.GetMappedPublicPort(15672)}";

    /// Polls the RMQ Management API until the named exchange has at least one publish_in.
    public Task<bool> WaitForExchangePublish(string exchange, TimeSpan timeout) { ... }
}
```

Per-service overrides (live in the consumer's `IntegrationTests` project):

```csharp
public sealed class PostgresFixture : PostgresContainerFixture
{
    protected override PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
        => builder
            .WithImage("postgres:16")
            .WithDatabase("AppDb")
            .WithUsername("app")
            .WithPassword("app");
}

public sealed class RabbitMqFixture : RabbitMqContainerFixture
{
    protected override RabbitMqBuilder Configure(RabbitMqBuilder builder)
        => builder
            .WithImage("masstransit/rabbitmq:3")
            .WithUsername("user")
            .WithPassword("secret");
}
```

A consumer can also skip the subclass and use the tech-specific base directly via `ICollectionFixture<PostgresContainerFixture>` if Testcontainers defaults are acceptable (random user/pass/db, image latest).

### `IntegrationFactoryBase<TProgram>`

A thin `WebApplicationFactory<TProgram>` wrapper that exposes a hook for wiring container connection details into the host config. **It does not own the containers** — those are class/collection fixtures provided by the consumer. This separation lets tests share container lifetime across many test classes.

```csharp
public abstract class IntegrationFactoryBase<TProgram>
    : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected abstract IDictionary<string, string?> EnvOverrides();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        foreach (var (k, v) in EnvOverrides())
            builder.UseSetting(k, v);
    }
}
```

Per-service factory wires its specific env keys (lives in the consumer's project):

```csharp
public sealed class ServiceFactory(
    PostgresFixture pg,
    RabbitMqFixture mq)
    : IntegrationFactoryBase<Program>
{
    protected override IDictionary<string, string?> EnvOverrides() => new Dictionary<string, string?>
    {
        ["DB_HOST"] = pg.Container.Hostname,
        ["DB_PORT"] = pg.Container.GetMappedPublicPort(5432).ToString(),
        ["DB_NAME"] = "AppDb",
        ["DB_USER"] = "app",
        ["DB_PASSWORD"] = "app",
        ["MESSAGEBUS_HOST"] = mq.Container.Hostname,
        ["MESSAGEBUS_PORT"] = mq.Container.GetMappedPublicPort(5672).ToString(),
        ["MESSAGEBUS_USERNAME"] = "user",
        ["MESSAGEBUS_PASSWORD"] = "secret",
        ["RUN_MIGRATIONS"] = "true",
    };
}
```

Each service's env-var keys differ (one might use `SHARDDB_*`, another `ORACLEDB_*`, etc.), so the abstract `EnvOverrides()` hook keeps the base ignorant of any specific naming convention. The consumer maps its own env keys onto the running container's hostname/port.

## Project layout (per consuming service)

```
tests/
├── <Service>.UnitTests/                    (existing — pure logic, no infra)
└── <Service>.IntegrationTests/             (new)
    ├── Fixtures/
    │   ├── PostgresFixture.cs              extends PostgresContainerFixture, configures builder
    │   ├── RabbitMqFixture.cs              extends RabbitMqContainerFixture, configures builder
    │   └── ServiceFactory.cs               extends IntegrationFactoryBase<Program>, maps env keys
    ├── Outbox/
    │   └── OutboxDispatchTests.cs          domain event → outbox → MQ round-trip
    ├── Endpoints/
    │   └── <Aggregate>EndpointTests.cs     HTTP contract per route
    ├── Consumers/                          (Discord / Sentinel)
    │   └── <Event>ConsumerTests.cs         bus → consumer → side-effect
    └── Migrations/
        └── MigrationSmokeTests.cs          empty DB → ApplyMigrations → no errors
```

## Per-service scope

| Service | Postgres | RabbitMQ | HTTP stubs | Test categories |
|---|:---:|:---:|---|---|
| Shard | ✓ | ✓ | SwgohApi | Outbox, Endpoints, Migrations |
| Oracle | ✓ | ✓ | SwgohApi | Endpoints, Migrations |
| Tournament | ✓ | ✓ | SwgohApi | Outbox, Endpoints, Migrations |
| Sentinel | ✓ | ✓ | DataProvider, Tournament | Outbox, Endpoints, Consumers, Migrations |
| Sandbox | ✓ | — | (optional) | Endpoints, Migrations, Stat-calc fixtures |
| Discord | — | ✓ | Sentinel, Oracle, Counter, Shard, Tournament, Sandbox | Consumers, HTTP-client retry |
| SharedKernel.UnitOfWork | ✓ | — | — | Outbox interceptor + ProcessOutboxMessagesJob with real DB |

## Sample test (outbox round-trip — regression cover for the 1.15.0 dispatch bug)

```csharp
[Collection(nameof(IntegrationCollection))]
public sealed class OutboxDispatchTests
{
    private readonly ServiceFactory _factory;
    private readonly RabbitMqFixture _mq;

    public OutboxDispatchTests(ServiceFactory factory, RabbitMqFixture mq)
    {
        _factory = factory;
        _mq = mq;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DomainEvent_PublishesIntegrationEventToExchange()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var aggregate = Aggregate.Create(...).Value;
        aggregate.RaiseSomeChange(...);
        ctx.Aggregates.Add(aggregate);
        await ctx.SaveChangesAsync();   // outbox row written by interceptor

        var received = await _mq.WaitForExchangePublish(
            "Some.Contract.Namespace:SomeIntegrationEvent",
            timeout: TimeSpan.FromSeconds(30));

        received.ShouldBeTrue();
    }
}

[CollectionDefinition(nameof(IntegrationCollection))]
public sealed class IntegrationCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RabbitMqFixture>,
      ICollectionFixture<ServiceFactory>;
```

Container lifetime is **per-collection**, not per-test. All tests in the collection share one Postgres + one RabbitMQ — start cost paid once per `dotnet test` invocation, not per `[Fact]`.

## Phased rollout

| Phase | Scope | Effort |
|---|---|---|
| **1** | Build `Resrcify.SharedKernel.IntegrationTesting` package: `PostgresContainerFixture`, `RabbitMqContainerFixture`, `IntegrationFactoryBase<TProgram>`, `WaitForExchangePublish` helper. Cover the SharedKernel.UnitOfWork outbox-dispatch path itself as the first consumer (real Postgres + fake bus). | 1–2 days |
| **2** | Shard `IntegrationTests` — most outbox use; first real cross-service consumer of the package. Establishes the per-service factory pattern. | 1 day |
| **3** | Tournament, Sentinel, Oracle (copy-paste-adapt from Shard, override images / env keys). | 1 day each |
| **4** | Sandbox (no MQ, simpler scope). | 1 day |
| **5** | Discord (consumer-focused; WireMock-heavy; no DB). | 1–2 days |

## CI integration

- Tag every integration test with `[Trait("Category", "Integration")]`.
- Existing `build-and-test.yml` filters them out: `dotnet test --filter "Category!=Integration"`.
- New `integration-tests.yml` per service runs after the unit-test workflow with `--filter "Category=Integration"`.
- GitHub Actions `ubuntu-latest` runners have Docker out of the box; no self-hosted runners needed.
- Cache pulled images per-service (`docker pull <image>` step) to shave ~30s per run.
- Start advisory (non-blocking) for ~2 weeks; promote to required-check once stable.

## Why this catches the bug we just shipped

The Phase 1 outbox round-trip dispatches a real `AlignmentChangedEvent` through the production code path (`InsertOutboxMessagesInterceptor` → `OutboxMessages` → `ProcessOutboxMessagesJob` → `IPublisher.Publish` → handler → `IPublishEndpoint.Publish` → RabbitMQ exchange). On stock 1.15.0 this test fails with `received = false` before any image is tagged. On 1.15.1 it passes. The unit-level regression already lives in `MediatorRuntimeTests.Publish_DispatchesByRuntimeType_WhenCallerHoldsBaseInterfaceVariable`; the integration variant covers the full production stack from interceptor to broker.
