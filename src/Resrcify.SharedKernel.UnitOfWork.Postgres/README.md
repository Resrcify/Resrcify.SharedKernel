# Resrcify.SharedKernel.UnitOfWork.Postgres

`Resrcify.SharedKernel.UnitOfWork.Postgres` sets a service's DbContext up on PostgreSQL the way every service does it,
in one call: the connection from the `Database` section, the SharedKernel interceptors built by the container, and
opt-ins for the outbox, retries and an outbox that wakes up as soon as a save commits. It also has the design-time
factory `dotnet ef` needs and an `xmin` concurrency token.

## Table of Contents

- [Resrcify.SharedKernel.UnitOfWork.Postgres](#resrcifysharedkernelunitofworkpostgres)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
  - [Quick Start](#quick-start)
  - [Configuration](#configuration)
  - [Options](#options)
    - [The outbox](#the-outbox)
    - [Waking the outbox on commit](#waking-the-outbox-on-commit)
    - [Retries](#retries)
    - [The service's own options](#the-services-own-options)
  - [Design-time factory](#design-time-factory)
  - [Optimistic concurrency with xmin](#optimistic-concurrency-with-xmin)
  - [Moving a service to it: before and after](#moving-a-service-to-it-before-and-after)
  - [Related modules](#related-modules)

## What you get

- `services.AddPostgresDbContext<TContext>(configuration, db => ...)` (`Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions`):
  - the connection from the `Database` section (`PostgresOptions`), validated when the host starts, the connection
    string built by `NpgsqlConnectionStringBuilder` (a password with a `;`, a quote or a space works);
  - `UpdateAuditableEntitiesInterceptor` and `UpdateDeletableEntitiesInterceptor` built by the container, so they read
    the registered `TimeProvider` (a `FakeTimeProvider` in tests);
  - `IUnitOfWork` as `UnitOfWork<TContext>` (unless one is registered);
  - opt-ins: `.WithOutbox()`, `.WithOutboxWakeUp()`, `.RetryOnFailure()`, `.ConfigureNpgsql(...)`,
    `.ConfigureDbContext(...)`, `.ConfigureConnection(...)`, `.FromSection(...)`.
- `provider.GetPostgresConnectionString<TContext>()` for what else needs the connection (a health check).
- `PostgresDesignTimeFactory<TContext>`: the factory `dotnet ef` uses, reading the same settings, in one line.
- `builder.HasPostgresRowVersion()`: optimistic concurrency on the `xmin` system column, without a new column.

## Prerequisites

- .NET 10 SDK, PostgreSQL.
- **The packages it uses, referenced by the service.** Like every SharedKernel package, this one keeps its
  dependencies private, so the service lists what it ships:

  | Package | Why |
  |---|---|
  | `Npgsql.EntityFrameworkCore.PostgreSQL` | the EF Core provider (its builder types are in this package's API) |
  | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational` | EF Core |
  | `Microsoft.Extensions.Hosting` | the configuration providers and options binding (a web host has them; a Persistence project running `dotnet ef` needs them) |
  | `Quartz` | only with `WithOutboxWakeUp()` and the outbox job (UnitOfWork's requirement already) |

  Every service's Persistence project references the first three already, and its Application project
  `Microsoft.Extensions.Hosting`.

## Install

```xml
<PackageReference Include="Resrcify.SharedKernel.UnitOfWork.Postgres" Version="<latest>" />
<!-- What it uses (its own references are private, so the service lists them): -->
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
<PackageReference Include="Microsoft.Extensions.Hosting" />
```

It references `Resrcify.SharedKernel.UnitOfWork`, which comes along.

## Quick Start

```csharp
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;

services.AddPostgresDbContext<ShardDbContext>(configuration, db => db
    .WithOutbox(outbox => outbox.OnConflictDoNothing = true)
    .WithOutboxWakeUp()
    .RetryOnFailure());
services.AddMigrationsOnStartup<ShardDbContext>(configuration);   // UnitOfWork: Migrations:Run
```

## Configuration

The section and keys the services already have, so switching needs no configuration change:

```json
"Database": {
    "Host": "sharddb",
    "Port": "5432",
    "Database": "ShardDb",
    "Username": "ShardUser",
    "Password": "K33pTr4ck",
    "CommandTimeoutInSeconds": 500
}
```

`Host`, `Database`, `Username` and `Password` are required, `Port` is a port (5432 by default; a string binds),
`CommandTimeoutInSeconds` is optional (Npgsql's 30 s when not set). A missing or wrong value fails the host's start
naming every key that is wrong (`Database:Password is required.`). Another section: `db.FromSection("Postgres")`.
With two contexts each reads its own section.

## Options

### The outbox

`.WithOutbox()` adds `InsertOutboxMessagesInterceptor`, built by the container with the registered
`IOutboxSerializer` (`SystemTextJsonOutboxSerializer` unless the service registers another, or passes one with
`outbox.Serializer`): the same one `AddOutboxProcessing` reads with. `outbox.OnConflictDoNothing = true` inserts with
`PostgresOnConflictOutboxInsertStrategy` (`ON CONFLICT ("DedupKey") ... DO NOTHING`, what the services use; needs
`PostgresOutboxIndexes.PartialUniqueDedup` on the outbox mapping). Processing stays `AddOutboxProcessing` or
`AddProcessOutboxMessagesJob`. A context without `.WithOutbox()` doesn't get the interceptor, even when another
context of the service has it.

### Waking the outbox on commit

`.WithOutboxWakeUp()` cuts the wait between a save and its events being handled from the polling interval to about
a tenth of a second (76 ms measured, with the job polling every 10 minutes):

- a save that wrote outbox messages sends `SELECT pg_notify('resrcify_outbox', '<DbContext>')` on its connection,
  after the outbox interceptor wrote them: inside the caller's transaction when there is one (PostgreSQL delivers a
  notification on commit, never on rollback), otherwise right after the save committed;
- a hosted service listens on one dedicated, unpooled connection (`LISTEN resrcify_outbox`) and, for its context,
  wakes the outbox (`OutboxWakeUp<TContext>`): the processing job runs now (Quartz `TriggerJob`) and every outbox lane
  polls now;
- cheap: notifications within `Debounce` (50 ms) wake once, and a wake-up while a run is already on its way doesn't
  queue another; the job then drains everything waiting while its batches come back full;
- robust: a lost connection is replaced (1 s doubling to 30 s), a dead one is noticed by Npgsql's keepalive (30 s),
  and every (re)connect wakes the outbox once, for what was saved meanwhile. Shutdown ends the wait;
- the polling stays as the safety net: a notification that is lost (the process died between the commit and the
  notify) only means the message waits for the next poll.

Needs `.WithOutbox()`. With several service instances every instance is woken, so set the outbox's `Claim`
(`PostgresOutboxLaneClaim.Instance`), as several instances need anyway.

### Retries

`.RetryOnFailure(maxRetryCount = 6, maxRetryDelay = 30 s)` uses Npgsql's retrying execution strategy
(`EnableRetryOnFailure`): a dropped connection, a failover, a serialization failure or a deadlock is tried again.
The unit of work runs under it: `CompleteAsync` and `TryCompleteAsync` save through the strategy, and
`ExecuteInTransactionAsync` (the transaction behavior, the outbox job and lanes) runs the whole transaction again.
Two things EF Core refuses under a retrying strategy, so change them before turning it on:

- a transaction begun by hand (`IUnitOfWork.BeginTransactionAsync` + `CommitTransactionAsync`, as Tournament's jobs
  do): use `ExecuteInTransactionAsync`;
- `DbContext.SaveChangesAsync()` of domain events with `OnConflictDoNothing` (that insert opens a transaction of its
  own): save through `IUnitOfWork`.

### The service's own options

```csharp
db.ConfigureNpgsql(npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "shard"))
  .ConfigureDbContext(options => options.EnableDetailedErrors())
  .ConfigureConnection(connection => connection.MaxPoolSize = 50);
```

## Design-time factory

`dotnet ef` needs to create the context without the host. One line instead of each service's 50:

```csharp
internal sealed class ShardDbContextFactory : PostgresDesignTimeFactory<ShardDbContext>;
```

It reads the Web project's `appsettings.json` and `appsettings.{environment}.json` (the Web project is found from the
Persistence project's name, `Titan.Shard.Persistence` → `Titan.Shard.Web`, as a sibling folder or under `src/`), then
environment variables (`Database__Host=localhost`), then the arguments after `--`
(`dotnet ef database update -- --Database:Host=localhost --Database:Port=5433`). The environment is
`ASPNETCORE_ENVIRONMENT`, else `DOTNET_ENVIRONMENT`, else `Development`. Missing values get placeholders, so
`migrations add` (which doesn't connect) works without settings. Override `Configure(PostgresDbContextBuilder)` to
apply the Npgsql options the registration uses (share a static method between the two), `ConfigureOptions` to
change the settings read (Sandbox points them at `localhost:5438`), `SettingsDirectory` to read settings elsewhere,
and `CreateDbContext(DbContextOptions<TContext>)` for a context without a public `(DbContextOptions<TContext>)`
constructor.

## Optimistic concurrency with xmin

```csharp
builder.HasPostgresRowVersion();                    // a shadow property, the entity class doesn't change
builder.HasPostgresRowVersion(shard => shard.Version); // or a uint property of its own
```

PostgreSQL changes a row's `xmin` system column on every update, so it serves as the concurrency token without a
column of its own (mapped as Npgsql recommends: a `uint`, `IsRowVersion()`, column `xmin` of type `xid`; migrations
don't create it). A save that updates or deletes a row someone changed since it was read throws
`DbUpdateConcurrencyException`; `IUnitOfWork.TryCompleteAsync` returns it as `PersistenceErrors.Concurrency` (a
`Conflict`, 409), and the mediator returns it as the command's result with
`cfg.ConfigureUnitOfWork(uow => uow.ReturnPersistenceFailures = true)`. Adding it changes the model snapshot only:
add a migration (it is empty).

## Moving a service to it: before and after

Before (Shard; every service has its own copy):

```csharp
// PersistenceServiceRegistration
services.AddOptions<PersistenceOptions>().BindConfiguration(PersistenceOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();
services.AddOptions<MigrationsOptions>().BindConfiguration(MigrationsOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();
services.AddSingleton<IOutboxSerializer, SystemTextJsonOutboxSerializer>();
services.AddDbContext<ShardDbContext>((sp, options) =>
{
    var db = sp.GetRequiredService<IOptions<PersistenceOptions>>().Value;
    var serializer = sp.GetRequiredService<IOutboxSerializer>();
    options.UseNpgsql($"host={db.Host};port={db.Port};database={db.Database};username={db.Username};password={db.Password};");
    options.AddInterceptors(
        new InsertOutboxMessagesInterceptor(serializer, new PostgresOnConflictOutboxInsertStrategy()),
        new UpdateAuditableEntitiesInterceptor(),
        new UpdateDeletableEntitiesInterceptor());
});
services.AddScoped<IUnitOfWork, UnitOfWork<ShardDbContext>>();
var migrations = configuration.GetSection(MigrationsOptions.SectionName).Get<MigrationsOptions>();
if (migrations?.Run == true)
    services.ApplyMigrations<ShardDbContext>();

// ShardDbContextFactory.cs: 45 lines re-reading the Web project's appsettings
```

After:

```csharp
// PersistenceServiceRegistration
services.AddPostgresDbContext<ShardDbContext>(configuration, db => db
    .WithOutbox(outbox => outbox.OnConflictDoNothing = true)
    .WithOutboxWakeUp());
services.AddMigrationsOnStartup<ShardDbContext>(configuration);

// ShardDbContextFactory.cs
internal sealed class ShardDbContextFactory : PostgresDesignTimeFactory<ShardDbContext>;

// InfrastructureServiceRegistration: the health check reads the same connection
.AddNpgSql(sp => sp.GetPostgresConnectionString<ShardDbContext>(), name: "postgres", tags: ["ready"])
```

Then delete `PersistenceOptions` and `MigrationsOptions`. The configuration doesn't change. Sentinel and Tournament
move their `commandtimeout=500` / `300` to `Database:CommandTimeoutInSeconds`.

## Related modules

- `Resrcify.SharedKernel.UnitOfWork`: the unit of work, the outbox, its jobs and lanes, `AddMigrationsOnStartup`, the
  Quartz helpers.
- `Resrcify.SharedKernel.Mediator`: `UnitOfWorkPipelineBehavior` (`ReturnPersistenceFailures`).
- `Resrcify.SharedKernel.IntegrationTesting`: `PostgresContainerFixture` for tests against a real PostgreSQL.
