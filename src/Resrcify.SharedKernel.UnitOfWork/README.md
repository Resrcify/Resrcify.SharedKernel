# Resrcify.SharedKernel.UnitOfWork

`Resrcify.SharedKernel.UnitOfWork` provides transaction orchestration, save coordination, and outbox processing helpers built around Entity Framework Core.

## Table of Contents

- [Resrcify.SharedKernel.UnitOfWork](#resrcifysharedkernelunitofwork)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
    - [Option A: Project reference](#option-a-project-reference)
    - [Option B: NuGet package](#option-b-nuget-package)
  - [Quick Start](#quick-start)
  - [Usage guide](#usage-guide)
    - [Complete unit of work](#complete-unit-of-work)
    - [Transactional scope](#transactional-scope)
    - [Persistence failures as results](#persistence-failures-as-results)
    - [Value object conversions](#value-object-conversions)
    - [Outbox table mapping](#outbox-table-mapping)
    - [Outbox serializer](#outbox-serializer)
    - [Wiring it up](#wiring-it-up)
    - [Interceptors from the container](#interceptors-from-the-container)
    - [Outbox processing job](#outbox-processing-job)
    - [Waking the outbox](#waking-the-outbox)
  - [Outbox lanes](#outbox-lanes)
  - [Migrations on start-up](#migrations-on-start-up)
  - [Quartz jobs](#quartz-jobs)
    - [Interval jobs](#interval-jobs)
    - [Command jobs](#command-jobs)
    - [Job heartbeats and liveness](#job-heartbeats-and-liveness)
  - [Related modules](#related-modules)

## What you get

- `IUnitOfWork` contract in `Resrcify.SharedKernel.Abstractions.UnitOfWork`.
- EF Core-backed `UnitOfWork<TDbContext>` implementation, with `TryCompleteAsync` returning a concurrency conflict,
  a duplicate or a serialization failure as a `Result` (`PersistenceErrors`).
- Outbox/background-job helpers in `BackgroundJobs/`: the outbox job (draining a backlog in one run), its lanes,
  `OutboxWakeUp`, and Quartz helpers (`AddIntervalJob`, `CommandJob`, job heartbeats and a liveness check).
- Interceptors for domain events and auditable/deletable entities, registrable in the container
  (`AddEntityInterceptors`, `AddOutboxInterceptor`).
- `AddMigrationsOnStartup<TDbContext>(configuration)`: migrations applied while the host starts.
- `AddSingleValueObjectConversions(assemblies)`: one EF Core convention instead of a `HasConversion` per value
  object property.
- On PostgreSQL, `Resrcify.SharedKernel.UnitOfWork.Postgres` sets the DbContext up in one call.

### Packages the service references

This package's own references are private, so a service references what it uses: `Microsoft.EntityFrameworkCore`
and `Microsoft.EntityFrameworkCore.Relational` (and its provider), `Quartz` for the outbox job and the Quartz helpers,
and `Microsoft.Extensions.Diagnostics.HealthChecks` for the health checks (an ASP.NET Core host has it).

## Prerequisites

- .NET 10 SDK.
- Entity Framework Core in the consuming project.
- Optional Quartz integration for scheduled outbox processing.

## Install

### Option A: Project reference

```xml
<ProjectReference Include="..\path\to\Resrcify.SharedKernel.UnitOfWork.csproj" />
```

### Option B: NuGet package

```xml
<PackageReference Include="Resrcify.SharedKernel.UnitOfWork" Version="<latest>" />
```

CLI:

```powershell
dotnet add package Resrcify.SharedKernel.UnitOfWork
```

## Quick Start

```csharp
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Primitives;

services.AddScoped<IUnitOfWork, UnitOfWork<AppDbContext>>();
```

## Usage guide

### Complete unit of work

```csharp
public sealed class CompanyService(
    IUnitOfWork unitOfWork)
{
    public async Task SaveAsync(
        CancellationToken cancellationToken)
    {
        await unitOfWork.CompleteAsync(cancellationToken);
    }
}
```

### Transactional scope

```csharp
await unitOfWork.BeginTransactionAsync(
    isolationLevel: IsolationLevel.ReadCommitted,
    commandTimeout: TimeSpan.FromSeconds(30),
    cancellationToken: cancellationToken);

try
{
    await unitOfWork.CompleteAsync(cancellationToken);
    await unitOfWork.CommitTransactionAsync(cancellationToken);
}
catch
{
    await unitOfWork.RollbackTransactionAsync(cancellationToken);
    throw;
}
```

A transaction begun this way can't be used with a retrying execution strategy (`EnableRetryOnFailure`): EF Core
refuses it. `CompleteAsync` outside a transaction, and `ExecuteInTransactionAsync`, run under the strategy.

Or let `ExecuteInTransactionAsync` own the begin / save / commit / rollback / dispose
cycle — the operation, its `SaveChanges`, and the commit all succeed together or the
whole transaction is rolled back:

```csharp
await unitOfWork.ExecuteInTransactionAsync(
    async token =>
    {
        // mutate tracked entities; everything here commits atomically
    },
    cancellationToken: cancellationToken);
```

Under a retrying strategy a transient failure (a deadlock, a serialization failure, a dropped connection, a failed
commit) runs the operation again on a cleared change tracker. That is only safe when the operation's entities are all
its own: when the DbContext tracked entities **before** the call, the failure is thrown instead (an
`InvalidOperationException` holding it), since a retry would lose them and still report success. Load what the
operation changes inside it.

Called while a transaction is already open (a command sent from a domain event handler the outbox runs), it joins
that transaction on a savepoint. A failure undoes the operation's work in the database and in the change tracker (rows
it inserted are untracked, rows it updated or deleted read again). It runs at the open transaction's isolation level
and throws when asked for a stricter one. A command timeout of `TimeSpan.Zero` or `Timeout.InfiniteTimeSpan` means no
timeout.

`ExecuteAsync(operation)` runs an operation without a transaction of its own and, when it fails (a failed result or an
exception), undoes in the change tracker what it changed and drops the domain events it raised, so a later save in the
scope doesn't save half of it; the mediator's unit-of-work behavior sends every command through it.

### Persistence failures as results

`TryCompleteAsync` saves like `CompleteAsync` and returns, instead of throwing, the failures a caller can answer
(`PersistenceErrors`; match on `Code`):

| Exception | Error | Type |
|---|---|---|
| `DbUpdateConcurrencyException` (a row changed since it was read) | `Persistence.Concurrency` | `Conflict` |
| `SQLSTATE 23505` (a unique constraint) | `Persistence.UniqueViolation` | `Conflict` |
| `40001` (a serialization failure), outside a transaction | `Persistence.SerializationFailure` | `Failure` (transient) |
| `40P01` (a deadlock), outside a transaction | `Persistence.Deadlock` | `Failure` (transient) |

Anything else still throws. After a refused save the pending changes are discarded (and their events dropped), so
the scope's next save doesn't send them again: read the data again to retry. The `SQLSTATE` is read from the BCL's
`DbException.SqlState`, anywhere among the exception's causes (under `DbUpdateException`,
`RetryLimitExceededException`, or the `InvalidOperationException` a non-retrying Npgsql strategy wraps a transient
failure in), so no provider is referenced. `Persistence.Concurrency` is a `Conflict` to an HTTP caller, but
`Error.IsTransient()` counts it as transient, so the message bus retries it in a new scope. A serialization failure or a
deadlock **inside** a transaction throws: it aborted the whole transaction, so only the transaction's owner can try
again (`ExecuteInTransactionAsync` under a retrying strategy does). The mediator's unit-of-work behavior uses it when
asked (`cfg.ConfigureUnitOfWork(uow => uow.ReturnPersistenceFailures = true)`), returning the failure as the
command's result. On PostgreSQL, `HasPostgresRowVersion()` (the Postgres package) gives an entity a concurrency token.

### Value object conversions

A single-value value object (an id, a name, an ally code) used to need a conversion on every property of its type:

```csharp
builder.Property(x => x.PlayerId)
    .HasConversion(x => x.Value, v => PlayerId.Create(v).Value)
    .HasMaxLength(PlayerId.MaxLength);
```

Opt the type in by implementing `ISingleValueObject<TSelf, TValue>` (Abstractions): a value object with the usual
`Value` and `public static Result<PlayerId> Create(string value)` needs nothing else. Then add the convention once, in
the context, and drop the `HasConversion` calls (keep the rest of each line):

```csharp
public sealed class PlayerId
    : ValueObject,
    ISingleValueObject<PlayerId, string>
{ ... }   // unchanged

protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    => configurationBuilder.AddSingleValueObjectConversions(typeof(PlayerId).Assembly);

builder.Property(x => x.PlayerId)
    .HasMaxLength(PlayerId.MaxLength);   // a property with nothing else to say needs no line at all
```

Every property of an opted-in type gets `SingleValueObjectConverter<TSelf, TValue>`: nullable ones, keys, composite
and alternate keys, foreign keys, indexed ones, and those of owned and complex types. Switching changes nothing a
service can see, and the tests prove it against the hand-written conversions (`SingleValueObjectConventionExtensionsTests`):

- **the model is the same**: the migrations differ finds no difference and the model snapshot `dotnet ef migrations
  add` would write is identical, on PostgreSQL and SQLite (column types, lengths, nullability, keys, indexes, foreign
  keys, value generation, comparers), so switching needs **no migration**;
- **queries are the same**: the same SQL for `==`, `Equals`, `== null`, `Contains` over a list of value objects,
  owned and complex properties, navigations, ordering and `Include`, and the same rows;
- **change tracking is the same**: an equal value object (another instance) is no change, another value is;
- **reads are the same**: `FromPersisted`, by default `Create(value).Value`, so a stored value that no longer
  validates throws the same `InvalidOperationException` while the row is read.

A value object can read stored values its own way by implementing `public static TSelf FromPersisted(TValue value)`
(e.g. without validating); reads then differ from `Create(v).Value`, which is the point. Types that don't implement
the interface are left alone, and a property's own `HasConversion` still wins (keep one that, say, stores `null` as
`0`). Not covered: elements of a primitive collection (`PrimitiveCollection(...)`): keep their
`ElementType(e => e.HasConversion(...))`. Value objects that are structs aren't picked up.

**Compiled models** (`dotnet ef dbcontext optimize`, then `options.UseModel(AppDbContextModel.Instance)`) work with
the convention, and `SingleValueObjectConventionExtensionsCompiledModelTests` proves it: it generates the compiled model
with EF Core's own generator (the one `optimize` calls), compiles and loads it, and compares it with the model the
context builds at run time: the same converters, the same SQL (SQLite and PostgreSQL), the same rows, `Find`, writes
read back, change tracking and the failure on a stored value that no longer validates.

- By default the compiled model creates the converter with `new SingleValueObjectConverter<X, TValue>()`.
- `--native-aot` (and so `--precompile-queries`, which needs it) writes each conversion out as C# in the service's
  assembly: `((ISingleValueObject<X, TValue>)v).Value` and `SingleValueObjectConverter.FromPersisted<X, TValue>(v)`,
  both public. EF Core 10.0 itself can't build the NativeAOT model's relational model for an owned type sharing its
  owner's table when the table has an index ("Sequence contains no elements"), with hand-written conversions as much
  as with the convention; the tests run that variant on the model without the owned type.
- Precompiled queries weren't run end to end in the tests: their generator is experimental (`EF9100`) and internal API
  (`EF1001`). They read through the same generated conversion, which now compiles.

### Outbox table mapping

Map the `OutboxMessage` table from `OnModelCreating`. The configuration adds a
composite index on `(ProcessedOnUtc, OccurredOnUtc)` that backs the polling query
(`WHERE ProcessedOnUtc IS NULL ORDER BY OccurredOnUtc`) on every relational provider:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyOutboxMessageConfiguration();
}
```

Pass `schema` to place the table outside the provider's default schema (the schema
is created automatically by migrations / `EnsureCreated`):

```csharp
modelBuilder.ApplyOutboxMessageConfiguration(schema: "messaging");
```

On providers that support them, use the index hook to add covering columns and a
partial filter so the query is served entirely from a compact index. For PostgreSQL:

```csharp
modelBuilder.ApplyOutboxMessageConfiguration(
    tableName: "OutboxMessages",
    schema: "messaging",
    configureUnprocessedIndex: index => index
        .IncludeProperties("Type", "Content")
        .HasFilter("\"ProcessedOnUtc\" IS NULL"));
```

### Outbox serializer

One `IOutboxSerializer` strategy serializes events on the write side (interceptor)
and deserializes them on the read side (job). `SystemTextJsonOutboxSerializer` is built in
and embeds the concrete type in the payload. Create one instance and share it so the two
sides cannot drift apart. (The Newtonsoft.Json serializer was removed in 4.0; SharedKernel
no longer depends on Newtonsoft.Json.)

### Wiring it up

`AddOutboxProcessing` registers the read side (unit of work, serializer, Quartz job).
Add the `InsertOutboxMessagesInterceptor` to the `DbContext` yourself, passing the
**same** serializer instance:

```csharp
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

var outboxSerializer = new SystemTextJsonOutboxSerializer();

services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(connectionString)
    .AddInterceptors(new InsertOutboxMessagesInterceptor(outboxSerializer)));

services.AddOutboxProcessing<AppDbContext>(
    outboxSerializer,
    options => options.MaxRetryCount = 3);

// Quartz hosts the registered job.
services.AddQuartz();
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```

### Interceptors from the container

An interceptor made with `new` reads the system clock unless handed one. Registered in the container, each is
built with the registered `TimeProvider` (and the outbox one with the registered serializer, insert strategy and
`IOutboxSaveObserver`s), once, for every context:

```csharp
services.AddEntityInterceptors();                 // auditable + soft delete
services.AddOutboxInterceptor();                  // SystemTextJsonOutboxSerializer unless one is registered
services.AddDbContext<AppDbContext>((provider, options) => options
    .UseNpgsql(connectionString)
    .AddSaveChangesInterceptors(provider));       // withOutbox: false for a context without the outbox table
```

The constructors are unchanged, so `new` still works. On PostgreSQL `AddPostgresDbContext` does all of this.

### Outbox processing job

The job reads a batch with a no-tracking projection (served by the covering index).
Each message is then handled in its **own DI scope and transaction** (via
`IUnitOfWork.ExecuteInTransactionAsync`): publishing the event and marking the
message processed commit together, so the message is only marked done if everything
its handlers persisted commits as well. Because every message gets a fresh scope, the
job never shares (or clears) a `DbContext` change tracker with other work. A handler
that throws does not abort the batch — the failure is written to the `Error` column
and `RetryCount` is incremented. When its last try (`MaxRetryCount`, or its lane's) fails, the message **gives up**:
its `ProcessedOnUtc` is set to `OutboxMessage.GivenUpProcessedOnUtc` (9999-12-31) and `Error` starts with
`Gave up at <time> after <n> tries.`. That takes it out of the unprocessed rows (and their index), so the polls no
longer read past it, and the cleanup keeps it for inspection. To try it again:
`UPDATE "OutboxMessages" SET "ProcessedOnUtc" = NULL, "RetryCount" = 0 WHERE "Id" = '<id>'`.

A run **drains a backlog**: while a batch comes back full it reads the next one, so 70 waiting messages with a batch
of 20 are handled in one run (four reads) instead of four runs. It stops starting batches after 80% of the trigger's
interval (`ProcessIntervalInSeconds`; a batch already started finishes), so a run ends before the next trigger is due.
A quiet outbox costs one query per run, as before. A message that fails in a run is not read again in that run: it
keeps its retry pace and waits for the next run.

### Waking the outbox

`OutboxWakeUp<TDbContext>` (registered with the job and the lanes) runs the processing job now (Quartz
`TriggerJob`) and lets every lane poll now; polling stays the safety net. Wake-ups coalesce: while a triggered run
hasn't started, another wake-up doesn't queue one more. On PostgreSQL, `WithOutboxWakeUp()` (the Postgres package)
calls it when a save commits outbox messages, through a `NOTIFY`; anything else can call `WakeAsync()` too. An
`IOutboxSaveObserver` registered in the container is told whenever a save wrote outbox messages.

### Cleaning up processed messages

Processed messages are deleted after **7 days** by an hourly job (`CleanupOutboxMessagesJob`), in batches of 5,000,
so the outbox table and its indexes don't grow forever. Messages not processed yet, and messages that gave up (their
`ProcessedOnUtc` is 9999-12-31, never before the cutoff), are kept. `AddOutboxProcessing` (`OutboxOptions.ProcessedRetentionInDays`) and
`AddProcessOutboxMessagesJob` (`processedRetentionInDays`) add it; 0 turns it off.

### Outbox lanes

Some events take long to handle. A scatter-gather event, for example, waits for replies from another
service. In the regular outbox job, which processes messages one at a time, such an event would hold up
every message behind it. An **outbox lane** moves those event types out of the regular job:

- **Registering a type.** Register an `IOutboxLaneEvent` singleton per event type
  (`services.AddSingleton<IOutboxLaneEvent>(new OutboxLaneEvent("reports", typeof(ReportRequested)))`). The message bus
  (`Resrcify.SharedKernel.MessageBus`, `AddScatterGather`) does this for you: every event that has an
  `IScatterGatherHandler` goes in its `scatter-gather` lane.
- **Stable IDs.** While a message is processed, `IOutboxMessageContext` (from `Resrcify.SharedKernel.Abstractions`)
  gives its handlers IDs that are the same on every retry of that message; the message bus uses them as the message
  IDs of the integration events a handler publishes, so subscribers can skip a repeat.
- **The regular job** skips lane event types, and keeps processing everything else in order.
- **Each lane** runs in a background service, with up to `MaxConcurrency` of its messages in flight. It
  polls every `PollInterval`, and at once when one of its messages finishes (so the freed slot takes the next) or the
  outbox is woken (`OutboxWakeUp`). A message another instance holds is looked at again at the next poll.
- **Processing is the same as the regular job:** own scope and transaction, publish, mark processed, and
  record the failure and retry up to `MaxRetryCount`.
- **Tracing:** every processed message, by the regular job or a lane, gets a span on
  `OutboxDiagnostics.ActivitySourceName`; whatever its handlers do (database, HTTP, bus sends) runs inside it.

`AddOutboxProcessing` runs the lanes with defaults (8 at a time, a 2 s poll). Tune them, or add them next
to a directly registered outbox job, with:

```csharp
services.AddOutboxLanes<AppDbContext>(lanes =>
{
    lanes.MaxConcurrency = 8;
    lanes.Claim = PostgresOutboxLaneClaim.Instance;   // safe with several service instances
});
```

A lane message holds a database connection and an open transaction while it is handled, so
`MaxConcurrency` also bounds the connections a lane uses. On PostgreSQL, `PostgresOutboxLaneClaim` locks
the message row (`FOR UPDATE SKIP LOCKED`) for that transaction, so another instance skips it instead
of processing it twice.

## Migrations on start-up

```csharp
services.AddMigrationsOnStartup<AppDbContext>(configuration);   // when Migrations:Run is true
```

A hosted service applies the pending migrations (`MigrateAsync`) in `IHostedLifecycleService.StartingAsync`, which
the host calls before it starts any hosted service: the web server, Quartz and the outbox lanes start on the migrated
schema. `Migrations:Run` is read when the host starts (missing or empty is `false`; anything but `true`/`false` fails
the start), so a value a test host adds counts. A failed migration is logged (Critical) and fails the start, rather
than serving on the old schema. Nothing is built during registration. Several instances starting at once are safe:
EF Core 9+ locks the database around `Migrate` (Npgsql: `LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE
MODE`), so a second instance waits and finds the migrations applied. EF Core creates the database and the history
table *before* it takes that lock, so instances starting together on a database that doesn't exist yet can collide
there (measured on PostgreSQL 18: `42P04 database "…" already exists` from `NpgsqlDatabaseCreator.CreateAsync`, about
one instance in 200 when 8 start at once). An instance whose migration fails because something already exists
(`SQLSTATE` 42P04, 42P06, 42P07, 42710 or 23505) logs a Warning and migrates again, up to 5 tries, waiting 0.2, 0.4,
0.8 and 1.6 s on the `TimeProvider`; that is what a restart would do, sooner, since `MigrateAsync` creates only what's
missing and applies, under the lock, only what nobody applied. Any other failure, or the fifth, fails the start. It
replaces `ApplyMigrations<T>()` (removed in 4.0), which built a second service provider during registration and
migrated synchronously before logging was up.

## Quartz jobs

### Interval jobs

```csharp
services.AddQuartz(quartz => quartz
    .AddJobHeartbeats()
    .AddIntervalJob<UpdateAllShardMembersJob>(TimeSpan.FromHours(6), startAfter: TimeSpan.FromMinutes(2))
    .AddIntervalCommandJob<RequestShardMemberRankSyncCommand>(TimeSpan.FromMinutes(1)));
```

`AddIntervalJob<TJob>(interval, startAfter = 30 s, timeProvider = DI's)` registers the job under its type name
(`IntervalJobSetup.JobKeyFor<TJob>()`; a generic job's type arguments follow a dash, as in
`ProcessOutboxMessagesJob-ShardDbContext`) with a trigger repeating every `interval`, first `startAfter` after the
scheduler is built. Runs never overlap (the job is registered as `DisallowConcurrentExecution`; mark the class too,
so it says so where it is written). It replaces a `*JobSetup` class per job. Cron schedules stay `AddJob` +
`AddTrigger`.

### Command jobs

Most jobs send one command. `AddIntervalCommandJob<TCommand>` needs no job class at all (`SendCommandJob<TCommand>`
sends a `new TCommand()`); a job that builds its command derives from `CommandJob<TCommand>` (or
`CommandJob<TCommand, TResponse>`) and overrides `CreateCommand(context)`. Either one:

- logs a failed result with its errors, at `Warning` when another try may pass (`IsTransient()`) and at `Information`
  when it is an expected answer; the trigger keeps its schedule;
- lets a cancellation (shutdown, an interrupt) through;
- logs any other exception at `Error` and throws it as a `JobExecutionException` (what Quartz expects from a job):
  listeners and Quartz's telemetry see the failed run, and the trigger keeps its schedule (no refire). The services'
  jobs either swallowed the exception after logging it (Quartz never knew) or let it out raw (Quartz logged it as an
  unhandled error).

### Job heartbeats and liveness

```csharp
services.AddQuartz(quartz => quartz.AddJobHeartbeats());
services.AddHealthChecks().AddQuartzJobs(tags: ["live"]);   // HealthTags.Live in the Web package
```

`AddJobHeartbeats()` stamps a heartbeat (`JobHeartbeatRegistry`) whenever any job fires or finishes: one listener, no
code in the jobs. `AddQuartzJobs(maxSilence?, name = "quartz-jobs", failureStatus = Unhealthy, tags?)` fails when a
scheduled job hasn't fired or finished for longer than `maxSilence(interval)` (by default three intervals plus two
minutes: a 1-minute job 5 minutes, a 15-minute job 47). A job's interval is read from its triggers (simple and cron
alike); a paused, finished or one-off trigger isn't checked, and a job not due yet isn't stalled. A job that fires
and fails keeps beating, so an upstream outage doesn't restart the pod. It is meant for `/health/live`: a scheduler
that stopped firing (Shard's rank updates froze for ~29 h) is fixed by a restart. A run counts as silence until it
ends, so give long-running jobs room (`maxSilence: interval => interval * 3 + TimeSpan.FromMinutes(30)`). Time is the
registered `TimeProvider`.

### Monitoring the outbox

Add the meter and the health check; the dashboard and alerts are in `Resrcify.Kubernetes/prod/messagebus-*.yaml`:

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(OutboxDiagnostics.MeterName));
builder.Services.AddHealthChecks().AddOutbox<ShardDbContext>(tags: ["ready"]);
```

| Metric | Type | Tags | What |
|---|---|---|---|
| `outbox.messages.handled` | counter | `context`, `event`, `outcome` | Messages handled: `processed`, `retrying` (failed, tried again later), `gave_up` (failed its last try; left as poison). |
| `outbox.message.duration` | histogram (s) | `context`, `event`, `outcome` | How long handling a message took. |
| `outbox.message.wait` | histogram (s) | `context`, `event` | How long a processed message waited in the outbox. |
| `outbox.messages.waiting` | gauge | `context` | Messages waiting to be processed. |
| `outbox.messages.poison` | gauge | `context` | Messages that gave up. |
| `outbox.oldest_waiting.age` | gauge (s) | `context` | How long the oldest waiting message has waited. |

The gauges come from a backlog monitor that measures every 30 s (`BacklogCheckIntervalInSeconds`), so neither a
scrape nor a health check queries the database. `AddOutbox<TDbContext>` fails (degraded by default) when the oldest
waiting message has waited longer than 5 minutes (`maxWaitingAge`; keep it above the processing interval) or when the
backlog can't be measured. Messages that gave up don't fail it; the `OutboxMessagesGaveUp` alert covers them.

### Time

Every timestamp and delay the outbox reads (`OccurredOnUtc`, `ProcessedOnUtc`, `CreatedOnUtc`, `DeletedOnUtc`, the
cleanup cutoff, the lanes' retry delays and polling) comes from `TimeProvider`: the one registered in DI (the system
clock by default), or the one passed to an interceptor's constructor. In tests, register a `FakeTimeProvider`
(Microsoft.Extensions.TimeProvider.Testing) and pass it to the interceptors you create (or let the container build
them: `AddEntityInterceptors` / `AddOutboxInterceptor`, see [Interceptors from the container](#interceptors-from-the-container)),
then move time with `Advance`:

```csharp
var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
services.AddSingleton<TimeProvider>(clock);
services.AddDbContext<AppDbContext>(o => o.AddInterceptors(
    new InsertOutboxMessagesInterceptor(serializer, timeProvider: clock),
    new UpdateAuditableEntitiesInterceptor(clock)));

clock.Advance(TimeSpan.FromDays(8));   // e.g. past the 7-day cleanup
```

## Related modules

- `Resrcify.SharedKernel.Mediator` includes `UnitOfWorkPipelineBehavior<,>` and `TransactionPipelineBehavior<,>` built on this module.
- `Resrcify.SharedKernel.DomainDrivenDesign` provides domain event abstractions consumed by outbox processing.