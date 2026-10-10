# Changelog

All notable changes to the Resrcify.SharedKernel packages. Versions follow [Semantic Versioning](https://semver.org/);
every package in the repository is released together under one version.

## 4.0.0 (unreleased)

Upgrading a service: work through **Breaking changes** below, top to bottom. The step-by-step guide for our services
(including the move from MassTransit) is `docs/migrating-from-masstransit.md` in the SwgohApi repository, "Step 0".

### Breaking changes

- **`MediatorConfiguration` no longer exposes its state**: `Assemblies`, `OpenBehaviorTypes`,
  `OpenBehaviorRegistrations` (and the `OpenBehaviorRegistration` type), `NotificationPublishStrategy`,
  `UseDiTimePipelineComposition`, `MediatorLifetime`, `ChecksBehaviors` and the `LoggingOptions` / `UnitOfWorkOptions` /
  `IdempotencyOptions` objects are internal. Configure through its methods (`ConfigureLogging(...)`,
  `ConfigureUnitOfWork(...)`, `UseMediatorLifetime(...)`, …): an options object set directly wasn't marked configured, so
  another `AddMediator` call's defaults could win over it.
- **`DomainEventConverter` is sealed.**
#### Packages and namespaces

- **`Resrcify.SharedKernel.Messaging` is now `Resrcify.SharedKernel.Mediator`.** Replace the package, then rename
  the namespaces:

  | 3.x | 4.0 |
  |---|---|
  | `Resrcify.SharedKernel.Abstractions.Messaging` | `Resrcify.SharedKernel.Abstractions.Mediator` |
  | `Resrcify.SharedKernel.Messaging.*` | `Resrcify.SharedKernel.Mediator.*` |
  | `Resrcify.SharedKernel.Abstractions.Web` (`IEndpoint`) | `Resrcify.SharedKernel.Web.Abstractions` |

- **Packages no longer bring other SharedKernel packages along.** `Web` no longer references `Mediator`;
  `UnitOfWork` no longer references `Mediator` or `DomainDrivenDesign`; `Mediator` no longer references `Caching`.
  Reference every SharedKernel package a project uses directly (a missing-namespace error tells you which).
- **`Abstractions` no longer brings the ASP.NET Core shared framework** (`Microsoft.AspNetCore.App`), and so neither do
  `DomainDrivenDesign`, `Caching` or `Mediator`, which reference it: Domain and Application projects reference them,
  and shouldn't get ASP.NET Core. A library that used `ILogger<T>`, `IOptions<T>` or `IConfiguration` through it
  fails to compile (CS0234/CS0246): reference `Microsoft.Extensions.Logging.Abstractions`,
  `Microsoft.Extensions.Options` or `Microsoft.Extensions.Configuration.Abstractions` (or, in a web project,
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />`). `Web`, `Observability` and `IntegrationTesting` still
  bring the framework.
- **Newtonsoft.Json is gone.** `NewtonsoftJsonOutboxSerializer` is removed and no package references Newtonsoft.Json.
  Use `SystemTextJsonOutboxSerializer` (no service used the Newtonsoft one). `Resrcify.SharedKernel.MessageBus` still
  gets Newtonsoft.Json 13.0.4 through Rebus, which depends on it.

#### Results

- **A result's rules are enforced.** A failure needs at least one error and can't contain `Error.None` or `null`; a
  success has no errors. Breaking them throws `ArgumentException` (it used to make a malformed result); a JSON payload
  breaking them (`"errors":[null]`) is a `JsonException`. Look for
  `Result.Failure(x.Errors)` where `x` might be a success, and for `Error.None` used as a placeholder error.
- **`Result.Errors` is an `ImmutableArray<Error>`** (was `Error[]`), copied when the result is made, so nothing can
  change a result's errors. It reads like the array did (`Length`, `[0]`, `foreach`, LINQ) and still converts:
  `return failed.Errors;` answers with that failure from a method returning `Result<T>` (any `T`) or, new, `Result`;
  so does an `Error[]`. Passing it where an `Error[]` is expected needs `.ToArray()`. `Result.Failure` takes an
  `IReadOnlyList<Error>` (arrays, lists and `ImmutableArray` all work). `Match`'s `onFailure` receives an
  `ImmutableArray<Error>`: a method group taking `Error[]` must take `ImmutableArray<Error>`. Shouldly's
  `ShouldBeEquivalentTo` compares types, so `Errors.ShouldBeEquivalentTo(array)` fails; use `ShouldBe(array)`.
  It is a struct and never null: drop `Errors.ShouldNotBeNull()` (no longer compiles) and `Errors is null` checks.
- **`Error` → `string` (still implicit) and `Error.ToString()` give `"Code: Message"`**, not the code alone (the code
  alone when there is no message). ⚠️ Code that used an error AS ITS CODE still compiles and now gets the longer
  text: a comparison (`x.Code == someError`, `.Code.ShouldBe(someError)`), a dictionary key, a metric tag or a
  `new Error(code: someError, ...)`. Search for those and write `someError.Code`. An error passed as a message
  (`new Error("X", someError, ...)`) now reads `"Code: Message"` instead of the bare code.
- **The `Create` extensions are removed**; they did exactly what `Map` does. `.Create(f)` → `.Map(f)`.
- **`Match(func, error)`, which returned a `Result`, is now `Map(func, error)`.** It maps a success and replaces a
  failure's errors with `error`.
- **`Tap` on a `Result<T>` with a step returning a `Result` stops on that step's failure**, as the `Task` form always
  did. It used to ignore it.
- **`TryCatch` lets an `OperationCanceledException` through** instead of turning it into the error.

#### Web

- **`ResultExtensions` (Web) is split in two**, so it no longer clashes with the Results package's
  `ResultExtensions`: `HttpResultExtensions` (`ToProblemDetails`, `Match`) and `HttpResponseMessageExtensions`.
  Delete the `using ResultExtensions = Resrcify.SharedKernel.Web.Extensions.ResultExtensions;` alias and write
  `HttpResultExtensions.ToProblemDetails` where a method group named the class.
- **`HttpResponseMessage.Convert` / `Convert<T>` are `ToResultAsync` / `ToResultAsync<T>`.** They now also return a
  failure instead of throwing when the body isn't what they expect: `Http.<status>` (with the status's error type)
  for a failure without problem details, `Http.EmptyContent` for a 204 or a `null` body, `Http.UnreadableContent` for
  a success body that isn't JSON. The problem details and their errors are read the way SharedKernel writes them,
  whatever options are passed (those apply to the success body only, so the non-generic `ToResultAsync` takes none);
  an `errors` entry that is `null` or `Error.None` is dropped.
- **A status the result pattern doesn't name reads as the request's own fault, not a server failure:** a 408 is a
  `Timeout`, a 410 a `NotFound`, a 412 a `Conflict`, and any other 4xx (405, 413, 415, 422, …) a `Validation` error,
  so none but the 408 is transient; they were all `Failure`. A 5xx other than 502 and 504 is still a `Failure`.
- **A missing or invalid user id claim (`GetUserId`) is `Unauthorized` (401)**; it was `Validation` (400).
- **`ErrorType.Timeout` problem details are titled "Gateway Timeout"** (the status was already 504).

#### Caching

- **`ICachingService.SetAsync` with a `TimeSpan` is gone; the expiry is in the method's name.** A positional
  `TimeSpan` used to mean a *sliding* expiration, which few callers meant. Pick one:

  | Was | Now |
  |---|---|
  | `SetAsync(key, value, TimeSpan.FromMinutes(5), ct)` meant to expire after 5 min | `SetForAsync(key, value, TimeSpan.FromMinutes(5), ct)` |
  | `SetAsync(key, value, TimeSpan.FromMinutes(5), ct)` meant to stay while read | `SetSlidingAsync(key, value, TimeSpan.FromMinutes(5), ct)` |
  | `SetAsync(key, value, TimeSpan.MaxValue, ...)` (keep it until replaced) | `SetForAsync(key, value, <a real lifetime>, ct)`: see below |
  | `SetAsync(key, value, someDateTimeOffset, ct)` | unchanged |

- **Every cache entry expires, within a year.** There is no way to cache without an expiry any more: the implementation
  refuses one (`ArgumentException`), and any lifetime longer than `ICachingService.MaxLifetime` (365 days), whether
  relative (`SetForAsync`), sliding (`SetSlidingAsync`), an absolute date further away or a claim, is refused
  (`ArgumentOutOfRangeException`); `TimeSpan.MaxValue` and `DateTimeOffset.MaxValue` among them. A caching query whose
  `Expiration` is longer is cached for those 365 days.
  Data kept "until the next update replaces it" (DataProvider's base data and localisation, Sandbox's content
  versions and log settings) needs a lifetime longer than the update interval, and code that reads it must handle
  it being gone (re-fetch, or fail clearly), as it already must when the cache is restarted or evicts.
- An implementation of `ICachingService` implements
  `SetAsync(key, value, absoluteExpiration, absoluteExpirationRelativeToNow, slidingExpiration, serializerOptions, cancellationToken)`,
  now without defaults, with at least one expiration. `SetForAsync` passes a duration
  (`absoluteExpirationRelativeToNow`), which the cache measures on its own clock. It refuses a lifetime longer than
  `ICachingService.MaxLifetime` too.
- **Claims are an interface of their own, `IClaimStore`** (Abstractions.Caching): `TryClaimForAsync(key, expiresIn)`,
  set-if-absent with an expiry, and `ReleaseAsync(key)`. `ICachingService` (a JSON cache) doesn't have them, so a cache
  needn't implement claims it doesn't use. `DistributedCachingService` implements both; the message bus'
  `SkipDuplicateEvents` claims in the registered `IClaimStore`, or in the registered `ICachingService` when that is one
  too, so a service registering `DistributedCachingService` as its cache needs nothing more. A claim store over Redis
  claims with `SET … PX … NX`.

#### Mediator

- **The mediator can't be a singleton** (`AddMediator(ServiceLifetime.Singleton, ...)` and
  `UseMediatorLifetime(Singleton)` throw): it would resolve handlers and their scoped services from the root
  provider. Behaviors can still be singletons.
- **The transaction and unit-of-work behaviors rethrow a handler's exception as it is**; they used to wrap it in an
  `InvalidOperationException`. Code catching `InvalidOperationException` around `Send` must catch the real type.
- **`CachingPipelineBehavior` caches for `Expiration` from the time it caches** (`SetForAsync`). Reads no longer
  keep an entry alive. A query with an `Expiration` of zero or less isn't cached (nor looked up).
- **`LoggingPipelineBehavior` logs a transient failure at `Warning`.** A failure with an `IsTransient()` error
  (`Failure`, `ExternalFailure`, `Timeout`, `RateLimit`) is now `Warning`; one about the request (`NotFound`,
  `Validation`, `Conflict`, `Unauthorized`, `Forbidden`) stays at `Information` (an expected answer). Check log filters and alerts
  keyed on the level.
- **The assembly scan registers handlers only** (`IRequestHandler`, `IValueTaskRequestHandler`,
  `IStreamRequestHandler`, `INotificationHandler`). Pipeline behaviors (all five kinds) and pre/post-processors in
  scanned assemblies used to be registered too, running whether asked for or not, outermost, in reflection order. Now
  they run only when registered, in the order registered: open behaviors with `cfg.AddOpenBehavior(typeof(MyBehavior<,>))`
  (now also stream behaviors) or `cfg.AddStandardBehaviors(...)`; closed ones in the container
  (`services.AddTransient<IPipelineBehavior<MyRequest, Result>, MyBehavior>()`); processors with the new
  `cfg.AddRequestPreProcessor(type)` / `cfg.AddRequestPostProcessor(type)`, open or closed. Look for behaviors that
  existed only to be found by the scan (Sandbox: `GameGetGameDataPipelineBehavior<,>`).
- **`AddMediator` fails at start-up when a request asks for a missing behavior**: a scanned request implementing
  `ICachingQuery` or `ITransactionalCommand` with no registered behavior constrained to that interface throws
  `InvalidOperationException` (add the behavior, e.g. with `AddStandardBehaviors()`; `cfg.SkipBehaviorCheck()` if
  yours is registered after `AddMediator(cfg => ...)`; with `AddMediator(assemblies)`, register it before). The behavior
  must run for the request's handler kind: an `ICachingQuery` or `ITransactionalCommand` with a ValueTask handler
  (`IValueTaskRequestHandler`) needs a ValueTask behavior, since the standard ones run only for Task handlers.
  **⚠️ Handle each `ICachingQuery` deliberately; don't just register the behavior.** In 3.x one without the behavior
  was never cached, silently, so registering it now turns caching on for the first time. Before you do, check every
  such query: its `CacheKey` carries all its parameters (a fixed key answers every caller with the first one's
  result), its `Expiration` is a staleness the feature can live with, and its response serializes to JSON (the
  distributed cache stores JSON; a Discord `Embed` doesn't). A query that was never meant to be cached becomes a plain
  `IQuery` (Discord's seven did: they had never been cached).
- **`StandardBehaviorsOptions.InsertBefore` / `InsertAfter` take only `IPipelineBehavior<,>`s** and throw
  `ArgumentException` for another kind, which could only run elsewhere (an `IRequestPipelineBehavior` inside all of
  them, a ValueTask one only for ValueTask handlers). `AddOpenBehavior` orders behaviors within their kind.
- **Several `AddMediator` calls share their logging and unit-of-work options**: the call that configures them wins over
  the others' defaults, whatever the order (the first call used to win, dropping a later `ConfigureUnitOfWork`), and
  two calls configuring the same options throw.
- **A request's exception is logged at `Error` once**, with its stack, by the first behavior it leaves: the
  unit-of-work behavior for a plain command ("Request X threw Y; nothing it changed is saved"), the transaction
  behavior for a transactional one ("…; its transaction is rolled back", once whatever the retries of a retrying
  execution strategy), or the logging behavior for a query; the others log a `Debug` line without the stack. A request sent from a handler no longer logs its exception again in the sending request's behaviors. The
  messages "Exception caught in TransactionPipelineBehavior" / "...UnitOfWorkPipelineBehavior" are gone: check log
  filters and alerts matching them. A pipeline without the logging behavior still logs the exception.
- **A transactional command without a `CommandTimeout` keeps the DbContext's command timeout** (e.g.
  `Database:CommandTimeoutInSeconds`, or the provider's 30 s); the transaction behavior used to force 30 s. A command
  that relied on 30 s while its DbContext has a longer timeout gets the longer one: set `CommandTimeout` to keep 30 s.
- **A failed command's changes are undone in the change tracker.** The unit-of-work behavior runs a command through
  the new `IUnitOfWork.ExecuteAsync`: when it returns a failure (or its save is refused, or it throws), the entities it
  added are untracked, those it changed or deleted are put back, and the domain events it raised are dropped, so a
  later save in the same scope (the outbox marking the event processed, another command) doesn't save half of it.
  Changes pending before the command are kept.

#### Message bus

- **`HandleInPartitions` and `HandleOneAtATime` handle events in publish order by default** (`inPublishOrder: false`
  turns it off): an event takes its place in its partition in the order the bus received it; a failing event is
  retried in its place (5 tries, waiting 0.5–4 s) and then moved to the error queue, so the events after it of the
  same partition wait and then follow in order; and the service's RabbitMQ input queue gets a single active consumer
  (one instance consumes, the others stand by and take over if it stops). Order means the order the events reached
  the broker; an event that ends in the error queue is skipped. **A queue that already exists can't get the single
  active consumer flag**: RabbitMQ refuses the declaration and the bus doesn't start. Delete the drained queue, or use
  a new input queue name, when turning it on for a service that runs.

- **`IIntegrationEventHandler.HandleAsync` returns `Task<Result>`**, and event failures are handled without exceptions:
  a failure that is the event's fault (every error `NotFound`, `Validation`, `Conflict`, `Unauthorized`,
  `Forbidden`) is logged and not retried; any other failure, or an exception, is retried in place (as many tries as
  the bus' retry strategy allows, 5 by default) and then moved to the error queue. Return `Result.Success()` from a
  handler that has nothing to report.

- **Scatter requests are retried without exceptions, and a failure is always answered.** A responder's failure that is
  the request's fault (the same five error types) is answered at once; any other failure, or an exception, sends the
  request back to its queue for another try (any instance may take it, and it passes the rate limiter again, so retries
  spend the upstream's budget like any request), up to the bus' delivery attempts, 0.5–4 s apart, and is then answered
  with its errors. It used to be: throw to have Rebus retry, and a request that kept failing ended unanswered. So:
  - **return a failure instead of throwing** for what another try might fix, and type "no result" as `NotFound` (or
    another of the five) so it isn't retried;
  - **a requester that treated "answered" as "definite"** now also gets the given-up failures: use the gather's new
    `SettledKeys` (or `error.Type.IsTransient()`) to tell them apart;
  - the default mediator responder no longer throws (`AnswerFailure` now picks between answering at once and trying
    again first);
  - metrics: the `messagebus.requests.handled` outcome `error` is now `retried`, a request answered after its last try
    is `gave_up` (no longer `failure`), and `messagebus.scatter_gather.items` has `gave_up` too (no longer `failed`).

#### Unit of work and outbox

- **The outbox's Quartz jobs are keyed per DbContext**: `ProcessOutboxMessagesJob-<DbContext>`, plus the new
  `CleanupOutboxMessagesJob-<DbContext>`. Tests or tools that look a job up by name must use the new name.
- **Processed outbox messages are deleted after 7 days** by the new hourly cleanup job
  (`processedRetentionInDays`, 0 turns it off).
- **A new index, `IX_OutboxMessages_Processed`**, backs the cleanup, and **a new nullable column,
  `OutboxMessages.NextAttemptOnUtc`**, holds a lane message's next try: add an EF migration.
- **`IDeletableEntity.DeletedOnUtc` is `DateTime?`** (it was a `DateTime` holding `DateTime.MinValue` until deleted).
  Add a migration that makes the column nullable and sets never-deleted rows to `NULL`:
  `UPDATE "<Table>" SET "DeletedOnUtc" = NULL WHERE "DeletedOnUtc" = '-infinity' OR "DeletedOnUtc" = '0001-01-01 00:00:00+00';`
  (the sample's `UpgradeToSharedKernel4` migration shows it).
- **`SaveChanges` (synchronous) throws when aggregates raised domain events**, rather than dropping them. Save with
  `SaveChangesAsync`.
- **`UnitOfWork.Dispose` no longer disposes the DbContext** (the container owns it).
- **`IUnitOfWork.BeginTransactionAsync`'s `commandLifetime` parameter is `commandTimeout`.**
- **A message that gives up is marked, no longer left unprocessed**: its `ProcessedOnUtc` is
  `OutboxMessage.GivenUpProcessedOnUtc` (9999-12-31), and `Error` starts with "Gave up at … after n tries.". The polls
  no longer read past it, the cleanup keeps it, and the poison gauge counts it. Raising `MaxRetryCount` no longer
  retries it: set `ProcessedOnUtc` to `NULL` and `RetryCount` to 0. No schema change. Recommended data migration for
  rows that already gave up: `UPDATE "OutboxMessages" SET "ProcessedOnUtc" = '9999-12-31T00:00:00Z' WHERE
  "ProcessedOnUtc" IS NULL AND "RetryCount" >= <MaxRetryCount>;` (per lane with its own limit).
- **`ApplyMigrations<T>()` is removed.** Replace `if (migrations.Run) services.ApplyMigrations<TContext>();` (and the
  `MigrationsOptions` read before it) with `services.AddMigrationsOnStartup<TContext>(configuration);`: it migrates in
  `StartingAsync`, before anything serves, reads `Migrations:Run` at start-up, and fails the start when a migration
  fails. The old method built a second service provider during registration and migrated synchronously before logging
  was up.
- **`IUnitOfWork` has `TryCompleteAsync` and `ExecuteAsync`**; an implementation (a fake or a stub) must implement
  them. A substitute of `IUnitOfWork` must run the operation `ExecuteAsync` is given (the unit-of-work behavior sends
  every command through it).
- **`TryCompleteAsync` stops tracking what the database refused**: after a `Conflict` (a concurrency conflict, a
  unique violation) or a transient failure, the pending changes are discarded and their domain events dropped, so the
  scope's next save doesn't send them again. Read the data again to retry.
- **`ExecuteInTransactionAsync` under a retrying strategy no longer retries when the DbContext tracked entities before
  the call**: a retry clears the change tracker, which would lose them and still report success. The transient failure
  is thrown as an `InvalidOperationException` holding it; load the entities inside the operation, or send the command
  from a fresh scope. Nothing tracked before (the usual case, and the outbox's): retried as before.
- **`ExecuteInTransactionAsync` inside an open transaction throws when asked for a stricter isolation level** (e.g. a
  `Serializable` command sent from a handler the outbox runs at read committed); it used to run at the open
  transaction's level without a word. Send it outside that transaction, or open the outer one at that level.
- **A command timeout of `TimeSpan.Zero` or `Timeout.InfiniteTimeSpan` means no timeout again** (it became 1 s); a
  negative one throws.
- **A concurrency conflict is transient to the message bus**: `PersistenceErrors.Concurrency` stays a `Conflict` (a 409
  to an HTTP caller), but the bus retries an event whose handler returns it (in a new scope, which reads the data
  again), and a rate-limited queue no longer answers it as settled. The new `Error.IsTransient()` decides this
  everywhere (bus retries, log levels, `SettledKeys`); `ErrorType.IsTransient()` is unchanged.
- **The outbox job drains a backlog in one run**: it keeps reading batches while they come back full, for up to 80% of
  its interval, so a run can last longer than one batch. A test counting what one run processes must expect the whole
  backlog. A message that fails in a run isn't tried again in the same run.
- **Outbox lanes poll as soon as a slot frees up or the outbox is woken**; `PollInterval` is now the longest wait after
  a poll, not a fixed cadence. A message another instance holds is looked at again after `PollInterval`.
- `InsertOutboxMessagesInterceptor`, `ProcessOutboxMessagesJob` and `UnitOfWorkPipelineBehavior` have new optional
  constructor parameters; code compiles unchanged but must be recompiled.
- **`UnitOfWork.CompleteAsync` saves through the context's execution strategy** outside a transaction, so
  `RetryOnFailure` works with the `ON CONFLICT DO NOTHING` outbox insert; without retries nothing changes.

#### Domain-driven design

- **`IAggregateRoot` has `RemoveDomainEvent(domainEvent)`** (the unit of work drops a failed command's events with it);
  `AggregateRoot<TId>` implements it, a type implementing `IAggregateRoot` itself must too.
- **`Enumeration<T>` is stricter**: two members with the same value, or names that differ only in case, throw when
  the type is first used. `Register` is removed (members are found by reflection).
- **`ValueObject` equality is by type as well**: two value objects of different types are never equal, even with
  the same components. (`Entity` and `ValueObject` `==` also handle `null` on either side now.)

#### Repository

- **`IRepository<TEntity, TId>` has the fetches** (`GetByIdAsync`, both `FirstOrDefaultAsync`), returning `TEntity?`.
  `INullableFetchRepository` is gone: a repository interface derives from `IRepository` alone. Delete
  `INullableFetchRepository<…>,` (or `, INullableFetchRepository<…>`) from each repository interface.
- **`ResultRepository` and `IResultFetchRepository` are removed** (no service used them). Whether "not found" is an
  error is the handler's decision: it turns `null` into its own error with `ToResult` / `ToResultAsync`, e.g.
  `await shards.GetByIdAsync(id, ct).ToResultAsync(DomainErrors.Shard.NotFound(id))`.

#### Test fixtures

- **`PostgresContainerFixture` and `RabbitMqContainerFixture` default to `postgres:18` and
  `rabbitmq:4.3.4-management`** (was `postgres:15.1` and `rabbitmq:3.11`): what production runs, the RabbitMQ Cluster
  Operator v2.23.0's default image. Each 4.x refuses more deprecated features (4.1 accepted the queue 4.3 refuses), so
  the image moves with the operator.
  `RESRCIFY_TEST_POSTGRES_IMAGE` / `RESRCIFY_TEST_RABBITMQ_IMAGE` replace the image for every fixture.
- ArchitectureTesting: `SharedKernelPackage` has new members, `Observability` (after `Web`) and `UnitOfWorkPostgres` (after `UnitOfWork`),
  so the numeric values of the members after them shift.

### Added

- **`ErrorType.Unprocessable`** (`Error.Unprocessable(code, message)`): a well-formed request that breaks a rule of the
  operation, answered **422** (problem details) and read back from a 422. Not transient: the message bus and the
  resilience policy treat it as an answer. A 422 from another service used to read as `Validation`.
- **`ErrorTypeJsonConverter`**: error types are read by name or number, and one this version doesn't know (a newer
  service's) reads as `Failure` instead of failing the read. Used by the bus' failure replies and `ToResultAsync`.
- **`Resrcify.SharedKernel.MessageBus.Testing`, a test harness for the message bus.** `AddMessageBusTestHarness()`
  switches every bus of a service to an in-memory network (whatever transport it was given) and records what they
  published, sent, consumed, failed and dead-lettered, with waits that complete when the message arrives
  (`harness.Published.WaitForAsync<T>(match)`). `WaitUntilIdleAsync()` returns once no bus is handling a message and no
  queue holds one; `network.StartResponderAsync<TRequest, TResponse>(...)` stands in for another service's rate-limited
  queue; `FailNext<T>(deliveries)` and `FailStart(queue)` inject failures.
- **Endpoints for a mediator request** (Resrcify.SharedKernel.Web): `MapGetRequest`, `MapPostRequest`,
  `MapPutRequest`, `MapPatchRequest`, `MapDeleteRequest` and `MapRequest(pattern, methods, ...)`. The delegate is an
  ordinary Minimal-API handler returning any request answered with a `Result` (a command, a query, one of your own);
  the endpoint sends it and answers 200 with the value or 204, `onSuccess`'s typed result (201, 202, part of the
  value), or problem details (`onFailure`, or `RequestEndpointOptions` for the app), and declares its responses for
  OpenAPI. A delegate returning anything else fails when the endpoint is mapped.
- **Idempotent requests** (Resrcify.SharedKernel.Mediator): a request implementing `IIdempotentRequest` and sent
  again with its `IdempotencyKey` is answered with the first result instead of being handled again, however it
  arrives (HTTP, a consumer, a job). The new standard behavior `StandardBehavior.Idempotency` (right after logging: before validation, so a repeat gets
  the first answer though a rule reading the data would refuse it now, and before
  the transaction, so a result is kept only once committed) keeps results 24 hours (`ICachingService`) and holds the
  key while the first is handled (`IClaimStore`): a repeat meanwhile is a Conflict (409), the key used for a different
  request Unprocessable (422). Transient failures aren't kept. `IdempotencyScope` keeps keys per caller;
  `cfg.ConfigureIdempotency(...)` sets the expiry. The behavior check fails the registration of an idempotent request
  without the behavior. Over HTTP, `IdempotencyHeaders.Key` names the header and the request endpoints mark a replayed
  answer `Idempotency-Replayed: true` (`IIdempotencyContext`).
- **Outbox administration**: `OutboxAdministration<TDbContext>` (registered with the outbox) summarizes what waits,
  retries and gave up per event type and lane, lists the messages that gave up, shows one with its content, and tries
  one or all of them again. The new **Resrcify.SharedKernel.UnitOfWork.Web** package maps it to admin endpoints:
  `app.MapOutboxAdministration<AppDbContext>().RequireAuthorization(...)` (authenticated callers only by default).
- **`OutboxWakeUp<TDbContext>.DrainAsync()`** for tests: wakes the outbox until no unprocessed message is due, so a
  test needn't poll for its handlers to have run.
- **`IGathered.SettledKeys`**: the items with a definite answer (a result, or a failure that is the request's fault),
  for an all-or-nothing gather that must not apply when an item's responder gave up.
- **`ForwardEvent<TEvent>(...)`** on the message bus builder: hands an integration event to the mediator as a
  notification or a command, so a handler that only does that needn't be written.
- **`messagebus.events.published` has a `via_outbox` tag**: `false` marks an event published outside the outbox (a
  command handler, a callback), which nothing retries if the publish fails. The dashboard splits it.
- **Outbox metrics and a health check.** The meter `OutboxDiagnostics.MeterName` counts messages handled by outcome
  (`processed`, `retrying`, `gave_up`), times handling and time in the outbox, and gauges the backlog (waiting,
  given up, oldest waiting age), measured every 30 s by a background monitor. `AddHealthChecks().AddOutbox<TDbContext>()`
  fails when the oldest waiting message has waited more than 5 minutes or the backlog can't be measured. The Grafana
  dashboard and two alerts (`OutboxBacklogOld`, `OutboxMessagesGaveUp`) are in `Resrcify.Kubernetes/prod`.
- **`TimeProvider` everywhere SharedKernel reads the clock**: the audit, soft-delete and outbox timestamps, the
  outbox cleanup and lanes, the mediator's logging, the message bus' deadlines, retries, gather timeouts, broker
  reconnects and health gate, and every duration a metric records. The system clock by default; register a
  `FakeTimeProvider` (and pass it to interceptors you construct) to control time in tests. The outbox's Quartz job
  setups (`AddProcessOutboxMessagesJob`, `AddCleanupOutboxMessagesJob`) take an optional `timeProvider` for their
  first start; Quartz 4 runs the scheduler itself on the `TimeProvider` registered in DI.

- **`Resrcify.SharedKernel.MessageBus`**: a message bus on Rebus (MIT) and RabbitMQ, or in memory:
  - scatter-gather requests through the outbox (`IScatterGatherClient`: `RequestAsync`, `GatherAsync`,
    `StreamAsync`), answered by `IRequestResponder`s on per-instance rate-limited queues, with a default responder that
    sends the request through the mediator;
  - publish/subscribe integration events (`IEventBus`, `IIntegrationEventHandler`), with partitions
    (`HandleInPartitions`: one at a time per key, in publish order), duplicate skipping through `ICachingService`
    (`SkipDuplicateEvents`, 3 h), and name-clash detection between publishers;
  - messages named by class name, a health check (`AddMessageBus`), metrics (`Resrcify.SharedKernel.MessageBus`
    meter), compression above 32 KB, and `MessageContract` checks for tests.
- **Outbox**: lanes (`IOutboxLaneEvent`), stable message IDs per outbox message (`IOutboxMessageContext`), claims
  for running several instances, lane backoff (5 s doubling to 5 min), the cleanup job.
- **Results**: `ErrorType.IsTransient()` (`Failure`, `ExternalFailure`, `Timeout`, `RateLimit`: another try may
  pass) and `Error.IsTransient()` (that, or a concurrency conflict, `ErrorTypeExtensions.ConcurrencyConflictCode`), the
  rule the message bus retries by and the mediator's logging picks its level by;
  a JSON converter, so a failed `Result` serializes (it used to throw) and old payloads still read;
  `Tap` with a step returning a `Result`; `TryCatch` with a `Func<Exception, Error>`; `ToResult` / `ToResultAsync`
  to turn a fetched value, or its absence, into a result, with the error given (`ToResultAsync(error)`) or made only
  when needed (`ToResultAsync(() => error)`, for an error with a formatted message).
- **Caching**: `SetForAsync` (expires a set time from now) and `SetSlidingAsync`.
- **ArchitectureTesting**: a rule for which layer may use which SharedKernel package (`SharedKernelPackage`, `Layers`).
- **Mediator**: tests for DI-time pipeline composition.
- **Mediator tracing and metrics**, for every `Send` whatever the behaviors: the `ActivitySource` and `Meter`
  `MediatorDiagnostics.ActivitySourceName` / `MeterName` (`Resrcify.SharedKernel.Mediator`). One span per request
  (outcome, error code and type, exception) and the histogram `mediator.request.duration` (s, tags `request`,
  `outcome`). A single check when nothing listens.
- **`AddStandardBehaviors()`** on the mediator configuration: Logging → Validation → Transaction → UnitOfWork →
  Caching, with `Without(...)`, `InsertBefore(...)` and `InsertAfter(...)` for a service's own behaviors.
- **`LoggingPipelineOptions`** (`cfg.ConfigureLogging(...)`): `RequestLevel` for the start/complete lines (default
  `Information`), `SlowRequestThreshold` (a Warning when exceeded; off by default). The logging behavior also logs a
  handler's exception at `Error`, letting it through unchanged.
- **`AddRequestPreProcessor` / `AddRequestPostProcessor`** on the mediator configuration; `AddOpenBehavior` takes
  stream behaviors.
- **`ResultFactory.Failure<TResult>(errors)` / `(error)`** (Results): a failed `Result` or `Result<T>` for code generic
  over the result type, built once per type.
- **`IClaimStore`** (claims, for doing something once); `DistributedCachingService` is one, atomic within one process.
- **`Resrcify.SharedKernel.Observability`**: one call, `services.AddServiceTelemetry(serviceName, configuration,
  configure?)`, wires a service's telemetry the same way everywhere:
  - OpenTelemetry tracing and metrics: ASP.NET Core (without the `/metrics` and `/health` requests, `UntracedPaths`),
    HttpClient, the runtime, every SharedKernel source and meter (`Resrcify.SharedKernel.*`: mediator, outbox, message
    bus), Rebus.Diagnostics and Quartz 4 (`"Quartz"`);
  - the Prometheus exporter, with `app.MapServiceMetrics()` mapping an anonymous scrape endpoint;
  - traces over OTLP when `Observability:OtlpEndpoint` is set (the services' existing section and key, now validated);
  - Serilog from the `Serilog` section, with its self-log to stderr and `TraceId`/`SpanId` on every event.
  Every part can be turned off; more sources and meters (e.g. `Npgsql`) and hooks for the tracer, the meter provider
  and Serilog go through `ServiceTelemetryOptions`. Its dependencies are private like every package's: the service
  references the OpenTelemetry and Serilog packages it wires (listed in the package README).
- **Web: `app.MapHealthEndpoints()`** maps `/health` (every check), `/health/ready` (checks tagged
  `HealthTags.Ready`) and `/health/live` (`HealthTags.Live`; Healthy without any), anonymous, with `HealthResponseWriter`
  (status, duration, and per check its status, description, data, error and tags) and no dependency on
  `AspNetCore.HealthChecks.UI.Client`. Paths via `HealthEndpointOptions`.
- **Web: `services.AddResultProblemDetails()`** answers an unhandled exception with the same problem details as a
  failed result: a 500 whose `errors` hold one `Failure` error `Unhandled` (`ResultExceptionHandler.ErrorCode`), so
  `ToResultAsync` reads it back. The exception's type, message and stack appear only in Development
  (`IncludeExceptionDetails`); it is logged once, at Error; a request the client aborted is a 499, logged at Debug.
  Keep `app.UseExceptionHandler()`.
- **Web: `httpClientBuilder.AddResultResilience(configure?)`** (on Microsoft.Extensions.Http.Resilience, which the
  service references) retries what the result pattern calls transient: a status whose `ErrorType` `IsTransient()`
  (5xx, 408, 429), network errors and timed-out attempts; three times by default, exponential with jitter, honouring
  `Retry-After`. Per-attempt timeout (10 s), total timeout (30 s), a circuit breaker; `AlsoRetry` / `NeverRetry` status
  lists (e.g. `AlsoRetry.Add(404)` where a service retried 404 with Polly). A `Retry-After` at least as long as what
  is left of the total timeout isn't waited for: the caller gets the 429/503 at once. Every wait runs on the
  container's `TimeProvider`.
- **Web: `services.AddResrcifyJwtBearer(configuration)`**: JWT bearer authentication against Resrcify.Identity from
  the `Jwt` section (`Authority`, `Issuer`, `Audience`, validated at start-up): keys from the authority's discovery
  document (JWKS); issuer, audience, lifetime and signature checked; bearer as the default scheme. The service
  references `Microsoft.AspNetCore.Authentication.JwtBearer`.
- **ArchitectureTesting:** `SharedKernelPackage.Observability`, not allowed in Domain, Application, Persistence or
  Presentation.
- **`Resrcify.SharedKernel.UnitOfWork.Postgres`**: `services.AddPostgresDbContext<TContext>(configuration, db => ...)`
  binds the `Database` section the services already have (`Host`, `Port`, `Database`, `Username`, `Password`, new
  `CommandTimeoutInSeconds`), validated at start-up; builds the connection string with `NpgsqlConnectionStringBuilder`
  (a `;` in the password works); adds the interceptors from the container (with its `TimeProvider`); and offers
  `.WithOutbox()`, `.WithOutboxWakeUp()`, `.RetryOnFailure()`, `.ConfigureNpgsql/ConfigureDbContext/ConfigureConnection`
  and `.FromSection(...)`. Also `GetPostgresConnectionString<TContext>()` (for the Npgsql health check),
  `PostgresDesignTimeFactory<TContext>` (a one-line `dotnet ef` factory reading the Web project's appsettings,
  environment variables and arguments) and `HasPostgresRowVersion()` (optimistic concurrency on `xmin`, no new column).
  Its dependencies are private: the service references `Npgsql.EntityFrameworkCore.PostgreSQL`, EF Core,
  `Microsoft.Extensions.Hosting` and `Quartz`.
- **Outbox wake-up** (`WithOutboxWakeUp()`): a save that writes outbox messages sends `NOTIFY resrcify_outbox` within
  its transaction (delivered on commit only); a listener on one dedicated connection (debounce, keepalive, reconnect
  backoff on the `TimeProvider`) runs the outbox job and wakes the lanes at once, with polling as the safety net.
  Measured: 76 ms from save to handled, against a 10-minute poll.
- **Unit of work**: `IUnitOfWork.TryCompleteAsync` and `PersistenceErrors` (a concurrency conflict or unique violation
  is a `Conflict`; a serialization failure or deadlock outside a transaction is a transient `Failure`, also when the
  Npgsql strategy wrapped it in an `InvalidOperationException`; anything else still throws);
  `IUnitOfWork.ExecuteAsync(operation)` (undoes a failed operation's changes in the change tracker); `AddEntityInterceptors()`, `AddOutboxInterceptor()`, `AddSaveChangesInterceptors(provider, withOutbox)`
  (interceptors from the container); `IOutboxSaveObserver`; `OutboxWakeUp<TDbContext>`; `OutboxJobs` is public;
  `AddMigrationsOnStartup<TContext>(configuration)`.
- **Mediator**: `cfg.ConfigureUnitOfWork(uow => uow.ReturnPersistenceFailures = true)` makes the unit-of-work behavior
  return persistence failures as the command's result (a 409 for a conflict) instead of throwing.
- **Quartz helpers** (UnitOfWork): `AddIntervalJob<TJob>(interval, startAfter, timeProvider)` and
  `AddIntervalCommandJob<TCommand>` (replacing per-job `*JobSetup` classes); `CommandJob<TCommand>` /
  `CommandJob<TCommand, TResponse>` / `SendCommandJob<TCommand>` (a failed result logged at a level chosen by
  `IsTransient()`, an exception turned into a `JobExecutionException`); `AddJobHeartbeats()` and
  `AddHealthChecks().AddQuartzJobs(tags: ["live"])`, a liveness check that fails when a job stops firing (lifted from
  Shard).
- **`ISingleValueObject<TSelf, TValue>`** (Abstractions): a value object stored as its one value. It asks for the
  `Value` and `static Result<TSelf> Create(TValue)` such value objects already have; `FromPersisted` reads a stored
  value and is `Create(value).Value` unless the value object implements it.
- **`configurationBuilder.AddSingleValueObjectConversions(assemblies)`** (UnitOfWork, in `ConfigureConventions`):
  every property of an opted-in type is stored as `Value` and read with `FromPersisted`, keys, foreign keys, indexes,
  nullable, owned and complex types included, replacing the per-property `HasConversion(x => x.Value, v =>
  X.Create(v).Value)`. The model, the SQL and the reads stay identical and no migration is needed (tested against
  hand-written conversions on Npgsql and SQLite). A property's own `HasConversion` still wins; elements of primitive
  collections keep their `ElementType(...)` conversion. Also `SingleValueObjectConverter<TSelf, TValue>`. It works in
  compiled models (`dotnet ef dbcontext optimize`), `--native-aot` included (the read goes through the public
  `SingleValueObjectConverter.FromPersisted<TSelf, TValue>`): tested by generating the model with EF Core's generator,
  compiling and loading it, and comparing SQL (SQLite and PostgreSQL), rows, `Find`, writes, change tracking and
  validation failures with the model built at run time. Precompiled queries read through the same conversion but
  weren't run end to end (their generator is experimental, EF9100). Known EF Core 10.0 limitation: a NativeAOT compiled
  model can't build the relational model of an owned type sharing its owner's table when that table has an index.
- **`ServiceHostFixture<TProgram>`** (IntegrationTesting): hosts a service's `Program` on its own PostgreSQL (and
  RabbitMQ when `CreateRabbitMq` returns one, optionally on a shared network as `postgres` / `rabbitmq`), handing the
  host `Database:*`, `MessageBus:*` and `Migrations:Run=true` through `UseSetting` instead of process-wide environment
  variables, with `ConfigureSettings` and `ConfigureTestServices` for the service's own. A `Program` that builds its
  own `ConfigurationBuilder` must read `builder.Configuration` to see them. Also `JoinNetwork(network, aliases)` on
  `PostgresContainerFixture` and `RabbitMqContainerFixture`.
- **Results: `LoggedExceptions`** (`Resrcify.SharedKernel.Results.Diagnostics`): `Claim(exception)`,
  `IsLogged(exception)`, `Release(exception)` and `DataKey`, the log-once convention the mediator's behaviors and the
  Web exception handler follow. A service's own log-and-rethrow code joins in by logging at Error only when `Claim`
  returns true; whoever handles the exception for good (answers it, records it) calls `Release`, so the same instance
  thrown again in a later request (a cached faulted task or `Lazy`) is logged at Error again. An
  exception with read-only `Data` is logged everywhere, as before.

### Fixed

- **A scatter-gather request sent while the reply bus restarts waits for it instead of throwing.** When a RabbitMQ node
  went down, the reply bus restarted, and a request sent meanwhile threw `InvalidOperationException` ("The
  scatter-gather transport has not started") out of `RequestAsync` / `GatherAsync` / `StreamAsync`. It now waits for
  the bus within its own timeout (which now runs from before the send), and its items end unanswered if the bus isn't
  back by then. Found stopping cluster nodes under traffic.
- **Scatter-gather starts on RabbitMQ 4.3.** Its reply queue was transient and not exclusive, which RabbitMQ 4.3
  refuses by default (`transient_nonexcl_queues`): the bus couldn't declare it, and the service failed to start after
  a minute of reconnecting. The queue is now durable and the broker deletes it 30 minutes after its instance stops
  using it (`x-expires`), so it also keeps the replies that arrive during a reconnect, which the auto-deleted queue
  lost. Found moving Shard's events to the bus (Discord's end-to-end tests ran RabbitMQ 4.3; the RabbitMQ Cluster Operator v2.23.0 deploys 4.3.4).
- **Found trying the 4.0 APIs in Shard:** idempotency runs before validation (a retry of "create the shard" was
  refused by the rule "the shard must not exist yet" instead of getting the first answer); it takes a cache that is
  also a claim store (`DistributedCachingService` registered as `ICachingService`) without registering it as one; the
  architecture rule "endpoints depend on the mediator" accepts the request endpoints (`MapPostRequest`, …); and a test
  stand-in's queue takes options (`StartResponderAsync(..., queueOptions: q => q.PerSecond = 100)`).
- **`RabbitMqConnection.ToString()` no longer prints the password.** As a record it printed every property, the
  password and the connection string holding it included, so logging it (or an exception message with it) leaked them.
- **A failed `ExecuteInTransactionAsync` no longer leaves its changes for the next save.** The rollback undid them in
  the database, but they stayed tracked (and their domain events raised): the scope's next save wrote them outside the
  transaction that refused them. They are now reverted, rows saved inside the transaction put back as the database has
  them, and the caller's changes from before the call kept as they were. (The mediator's pipeline was safe: its
  unit-of-work behavior already undid a failed command.)
- **An outbox save observer failing after the save committed no longer fails the save** (the caller would try again
  what is saved): it is logged. Inside the caller's transaction it still fails the save, which rolls back with it.
- **An outbox table not migrated to 4.0 turns the outbox health check unhealthy**: the backlog measurement reads
  `NextAttemptOnUtc` too (and reports `waiting_for_later_try`), instead of only every lane's poll failing.
- **Message bus, ordering and the broker:** an ordered subscription numbers its messages in delivery order (the
  waiting receives resumed in thread order, so events of one key were handled out of publish order under load); events
  waiting in a partition behind one that goes back to the queue go back too, so they are handled after it, not before
  its redelivery; the event subscriptions resolve the bus when the service starts, not when the host makes its hosted
  services, so events aren't handled before the migrations ran; an event is published under its runtime type's name
  (published through a base type it went to a topic nobody binds); an integration event implementing `IDedupable`
  gets its stable message ID from its key, so content computed while handling (a time) no longer gives a retried
  outbox message's event a new ID; a sender no longer fails to start when it declares another service's queue that
  service declared with `x-single-active-consumer` (an existing destination queue is left alone); the broker watcher
  connects as the buses do (`RabbitMqConnection.VirtualHost` and `UseTls`, new, and the strategy's new
  `ConfigureConnectionFactory`), and a refused connection is retried rather than failing the service; a name clash
  between two publishers is warned once per pair, not on every event. The README no longer promises that the
  service's own bus restarts at once after a RabbitMQ restart: Rebus resumes it within a minute.

- **Message bus, events:** each in-place retry of a handler runs in a DI scope of its own (a failed try's tracked
  changes and domain events were saved with the next try: two payouts for one) and a Rebus transaction of its own
  (what a failed try published went out too, one copy per try, even when the event was dead-lettered); a claim store
  that is out of reach no longer dead-letters events unhandled (they are handled, without skipping duplicates), and a
  claim that can't be released is logged instead of losing the redelivery; `SkipDuplicateEvents` refuses a time that
  isn't positive or is longer than `ICachingService.MaxLifetime`; the wait between tries stops doubling at 4 s, so a
  strategy with more tries no longer holds an event unacknowledged for minutes (past RabbitMQ's consumer timeout);
  an exception the mediator logged already is logged at Error once, not again on every try and at the dead-letter.

- **Message bus, scatter-gather:** a stream yields every reply accepted before its timeout, however slowly it is read
  (the replies still buffered were dropped); a stream can be read once (reading it again re-sent the batch: now it
  throws); an empty batch is answered at once (it waited the whole timeout); a timeout that can't be met (zero,
  negative, `Timeout.InfiniteTimeSpan`, over ~49 days) throws before anything is sent; two
  `IScatterGatherHandler`s with the same event, request and response types both run (only the last did); the reply
  bus starts before, and stops after, the hosted services registered before it (the outbox lanes gathered before it
  was up, and at shutdown after it stopped); a restart after RabbitMQ came back that fails is logged and tried again,
  and the bus' health check reports it, where it was lost and the bus stayed down; the bus' own failure reply carries
  its error type by name, so two services with different enum settings read it.
- **Message bus, rate-limited queues:** a bus that fails to start (RabbitMQ unreachable while the gate is healthy) is
  tried again (5 s doubling to 1 min) instead of faulting the host (which stopped the application, or left the queue
  unconsumed while reported as consuming); two queues for the same request and response types (a priority queue)
  each consume with their own handler (one wasn't consumed at all), and a queue name used twice throws; a responder
  that takes `IEventBus` (or anything needing `IBus`) gets the queue's bus (it threw on every try); a request's
  deadline is measured on the container's clock, which Rebus now uses too, and never exceeds the request's
  time-to-live (a test's `FakeTimeProvider` dropped every request); the responder's exception is logged at Error once
  (`LoggedExceptions`), not again on every try. `IRequestResponder`'s documentation says what a throw does (tried
  again, then answered `<TRequest>.ResponderFailed`), as the README did.

- **IntegrationTesting: a fixture that failed to start tears down what did start.** A container that was never built
  (an empty image name) made `DisposeAsync` throw `NullReferenceException`, and `ServiceHostFixture` stopped tearing
  down at the first failure, leaving the PostgreSQL container and the network behind; every step now runs and their
  failures are reported together.

- **UnitOfWork.Postgres: the outbox wake-up never fails a save that committed.** Outside a transaction, a notification
  that can't be sent (the connection can't be reopened, the pool is exhausted, the caller's token is cancelled) is
  logged and dropped; a reconnect failure used to escape and fail the command after its work was saved. A burst of
  notifications wakes the outbox once, as documented (it woke once per notification, 50 ms apart, so a listener fell
  behind above about 20 saves a second).
- **UnitOfWork.Postgres: each context's outbox is written its own way.** `WithOutbox(o => o.OnConflictDoNothing = true)`
  on one context used to set the insert strategy for every context (a plain one then failed with `42P10`, or under
  `RetryOnFailure` with "does not support user-initiated transactions"). Two contexts asking for different serializers
  throw at registration; the second one was ignored.

- **`AddQuartzJobs` judges an uneven schedule by the fire that is due**: a job is stalled when the fire due after its
  last one is late by more than `maxSilence(interval)` less one interval, the interval being that fire's own. A
  business-hours or weekday cron no longer fails all night (it used to apply the 5-minute daytime gap to the 15-hour
  night); an even schedule is judged as before.
- **A command job refuses a command that returns a value**: an `ICommand<int>` also passes for an `IRequest<Result>`,
  so `AddIntervalCommandJob<TCommand>` / `CommandJob<TCommand>` compiled and then failed every run with "No request
  handler registered". It now throws at registration, naming the form to use: the new
  `AddIntervalCommandJob<TCommand, TResponse>` / `SendCommandJob<TCommand, TResponse>`. A command job logs an exception
  the mediator logged already at `Debug`, and releases it (`LoggedExceptions.Release`).

- **`AddOutbox<TDbContext>()` fails as `Degraded` by default**, as documented (it was `Unhealthy`, so a backlog on a
  `ready`-tagged check took every replica out of service at once). It also fails once the backlog hasn't been measured
  for three intervals since the monitor started (no database, no outbox table), where it stayed Healthy; and its
  counts come from one snapshot, so `poison` is never off by a message saved meanwhile (nor negative).

- **The outbox lanes claim with `OutboxOptions.Claim`** (when `AddOutboxLanes` sets none of its own, in either order):
  they ran unclaimed, so two instances processed (and scatter-gathered) the same lane message.
- **An event type is in one outbox lane only**: a service's own `OutboxLaneEvent` moves it out of a package's lane
  (e.g. scatter-gather's), where it used to be processed by both; two lanes of the service's own throw at start-up.
- **A failed outbox try is counted in the database**, in one statement on a row still unprocessed: two instances no
  longer count two failures as one, nor mark given up a message the other has just published.
- **The outbox lanes' retry schedule holds across instances**: a failed try writes when the next one is due
  (`NextAttemptOnUtc`), and every instance waits it out, so three instances no longer spend a message's tries in two
  seconds of an outage, however long the message waited before its first try. A lane's `MaxRetryCount` of 39 or more no longer overflows the delay (it went on without one).
- **`PostgresOutboxLaneClaim` follows the outbox's column names** (a naming convention, `HasColumnName`); it hard-coded
  `"Id"` and `"ProcessedOnUtc"`.
- **`AddOutboxProcessing` refuses a `BatchSize` below 1** (and a non-positive interval or retry count), which made a run
  read empty batches until its time was up; and **schedules the outbox jobs on the clock registered before it** (or
  `OutboxOptions.TimeProvider`), so a `FakeTimeProvider` drives them as it drives `AddIntervalJob`.

- **The outbox interceptor writes a save's events once, whatever fails.** When its own transaction (an insert strategy
  writing outside `SaveChanges`, e.g. `OnConflictDoNothing`) failed to commit, a retrying `CompleteAsync` saved nothing
  and reported success; the change tracker is now put back and the retry writes it all. A save that failed without
  EF telling interceptors (a concurrency conflict, a cancellation) left its messages tracked (a resolved conflict saved
  the event twice) or its transaction open (the next save rolled back silently); it is now undone before the next save.
  Inside a caller's transaction, the rows such a strategy wrote are taken back when the save fails (a savepoint).
- **The events of one save are published in the order raised.** They shared one `OccurredOnUtc` and the outbox reads
  by it, with random ids breaking the tie; each now gets its own time, a microsecond apart.

- **A `Result` written with a naming policy reads back** with the same options (`SnakeCaseLower`, `KebabCaseLower`,
  `SnakeCaseUpper`): reading matched only `IsSuccess`/`Errors`/`Value` as written without one.
- **A transient failure under a retrying strategy is retried, not hidden by a failed rollback.** When the connection
  dropped mid-transaction (a failover, a restarted server) or the commit failed, rolling back threw too
  (`ObjectDisposedException`, "This NpgsqlTransaction has completed") and replaced the transient failure, so
  `EnableRetryOnFailure` never retried it; the rollback's failure is now dropped. The same on a savepoint.
- **A nested transactional command that fails undoes what it saved inside its savepoint in the change tracker too**:
  rows it inserted are untracked, rows it updated or deleted are read again, so the outer transaction no longer writes
  the rolled-back values back.
- The validation behavior ran a request's validators at once (unsafe with a shared DbContext) and without the
  cancellation token; it runs them one at a time with the token.
- DI-time pipeline composition created each request handler three times per request.
- **The mediator's per-request cost.** A send through a fresh scope (how a service sends: the mediator is transient)
  took ~980 ns and 9.6 KB with one behavior; with five behaviors it now takes ~180 ns and ~1.2 KB, about what
  MediatR 12 takes, and a warm send ~55 ns (about 3 times faster than MediatR) (`MediatorPerScopeBenchmarks`):
  - each mediator kept five caches sized by the core count that a mediator handling one call never read; it keeps
    one small slot per kind of call instead;
  - pipelines were composed up front as closures (two closures and two delegates per behavior, for one call); a
    pipeline is now walked by index, making only the `next` each behavior is handed;
  - the DI-composed runtimes are transient (scoped cost every scope its tracking dictionary), the notification
    publisher is a singleton (both are stateless), and a transient `ISender`/`IPublisher`/`IStreamSender` is built
    directly instead of through a factory resolving `IMediator` (a scoped mediator still shares one instance).
- Outbox: a failed save kept the events and wrote them again correctly with every insert strategy; a retried
  execution strategy no longer loses outbox messages; nested transactional commands use savepoints.
- Two events of the same name published from one outbox message could swap message IDs between attempts (and one be
  skipped as a duplicate); IDs now depend on the content.
- `HttpResponseMessage` conversion threw on an HTML error page, an empty body, or a 204.
- `ResultRepository` built its not-found error even when the entity was found.
- A save scanned the change tracker once per SharedKernel interceptor plus once in EF (4 full `DetectChanges` with
  all three). It now scans twice: 20,000 tracked, 1 modified went from 17.0 ms / 40 MB to 9.7 ms / 21 MB (3.97 ms /
  9.5 MB without interceptors).
- `DistributedCachingService` ignored the caller's `JsonSerializerOptions` for indentation and escaping, and copied
  every value once more. A 1.6 MB value: 7.9 ms / 5.8 MB → 2.7 ms / 1.6 MB.
- The outbox serializer went through a string and a cloned `JsonDocument` per event, and resolved the type by name on
  every read. Serialize 552 ns → 208 ns, deserialize 1.6 µs → 0.4 µs. The stored format is unchanged and existing rows
  read.
- Duplicate skipping (`SkipDuplicateEvents`) is atomic: a copy of an event already being handled on the instance is
  skipped, and the handled ID is claimed in one step (`TryClaimForAsync`). Before, two copies arriving together could
  both be handled. A message that leaves handling by an exception (a shutdown) releases its claim, so its redelivery is
  handled instead of being skipped and lost.
- A given-up outbox message with a `DedupKey` no longer blocks later events with the same key.
- Scatter-gather: counting a batch's replies took every lock of its dictionary per reply (1,000 replies: 7.2 ms →
  0.17 ms). A gathered batch also kept every reply in a channel nobody read, and a streamed batch could complete before
  its last arrival was written.
- **The message bus compresses at the fastest level.** Large bodies are gzipped at `CompressionLevel.Fastest` and
  unzipped into a buffer sized from the gzip trailer, by the bus's own pipeline steps instead of Rebus's
  `EnableCompression`. The wire format is unchanged (header `rbs2-content-encoding: gzip`, same threshold rule), so a
  service on Rebus's compression and one on this bus read each other's messages. A 755 KB message: zip 7.2 → 1.8 ms;
  unzip 2.7 MB → 0.74 MB allocated; the compressed body is larger (189 → 313 KB for that message). Every bus now reads
  a gzipped message whatever its own `CompressMessagesAbove`, and a handler no longer sees `rbs2-content-encoding` in
  the headers of a message that came compressed.
- `RetryOnFailure` with the `ON CONFLICT DO NOTHING` outbox insert threw "does not support user-initiated
  transactions" on `CompleteAsync`.
- Outbox lanes waited a full poll interval after a slot freed up before starting the next message.
- **`AddMigrationsOnStartup`: instances starting together on a database that doesn't exist yet all start.** EF Core
  creates the database and the history table before it takes its migration lock, and Npgsql 10 ignores only the
  unique violation on `pg_database`, so one instance could fail the start with `42P04: database "…" already exists`
  (measured: 20 of 4,240 instances with 8 starting at once; after the fix 0 of 4,800). A migration that fails because
  something already exists (`SQLSTATE` 42P04, 42P06, 42P07, 42710 or 23505) is logged at Warning and tried again, up to
  5 tries, 0.2 to 1.6 s apart on the `TimeProvider`; any other failure, or the fifth, still fails the start.
- **An exception thrown by a request sent through the mediator is logged at `Error` once**, not a second time by
  `AddResultProblemDetails()`'s exception handler: both follow Results' `LoggedExceptions`. Whoever logs first marks
  `Exception.Data["Resrcify.SharedKernel.Logged"]`; the handler then logs a `Debug` line without the stack and answers
  the same 500.

### Removed

- `Resrcify.SharedKernel.Messaging` (replaced by `Resrcify.SharedKernel.Mediator`).
- `NewtonsoftJsonOutboxSerializer` and the Newtonsoft.Json dependency.
- The Results `Create` extensions (use `Map`).
- `MigrationsExtensions.ApplyMigrations<T>()` (use `AddMigrationsOnStartup<T>(configuration)`).
