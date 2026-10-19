# Changelog

All notable changes to the Resrcify.SharedKernel packages. Versions follow [Semantic Versioning](https://semver.org/);
every package in the repository is released together under one version.

## 4.7.0 (2026-10-19)

### Breaking changes

- **Namespaces renamed, no forwarders** (ArchitectureTesting, UnitOfWork): update the `using`s — `ArchitectureTesting.Helpers` → `.Tests` (`BaseArchitectureTest`), `.Layering` (`Layers`, `LayerAssemblyResolver`),
  `.PackageRules` (`SharedKernelPackage`); `UnitOfWork.Extensions` → `.Outbox` (`OutboxOptions`, `OutboxLaneOptions`),
  `.Health` (`QuartzJobsHealthCheckOptions`).

### Added

- **`ConventionalApplicationTests.Handlers_Should_NotDependOnSenderOrMediator`: a request handler doesn't send
  requests** (Resrcify.SharedKernel.ArchitectureTesting). It fails for every mediator request handler
  (`ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>`, `IRequestHandler<,>`,
  `IValueTaskRequestHandler<,>`, `IStreamRequestHandler<,>`, and the scatter-gather request handlers
  `IScatterGatherRequestHandler<,,,>` / `IStreamingScatterGatherRequestHandler<,,,>`, which are sent like any request)
  with a constructor parameter (a primary constructor's included), field or property, its base classes' included, of
  `ISender` or `IMediator` (or of anything that is one, also wrapped: `Lazy<ISender>`, `Func<IMediator>`, an array),
  and names each by layer, handler and member (`Infrastructure: <Handler> → constructor parameter 'sender' (ISender)`).
  It checks every layer assembly, not only Application, since a handler can live in Infrastructure (a scatter-gather
  request handler) or anywhere the mediator scans. A request sent from inside a handler runs the pipeline again inside
  the first request's (validation, a second unit of work, …) and hides the workflow; only entry points send
  (endpoints, Quartz command jobs, bus consumers, UI handlers). Allowed: `IPublisher` (publishing a notification isn't
  sending a request); integration event handlers, scatter-gather event handlers, request responders and notification
  handlers, which aren't request handlers; pipeline behaviors; a type nested in a handler (a type of its own).

  Adopting it: apps that inherit `ConventionalApplicationTests` get the rule on upgrade; a handler that sends another
  request fails it — move the workflow into the handler through its abstractions, or send from the entry point. To keep
  one for now, override the test and skip it with the reason.
- **`ConventionalApplicationTests(IReadOnlyDictionary<string, Assembly> layerAssemblies)`**
  (Resrcify.SharedKernel.ArchitectureTesting): checks the given layer assemblies instead of the ones found by naming
  convention, as `ConventionalLayerDependencyTests` already could.
- **Waiting for a signal: `HangGuard`, `AwaitableLog<T>`, `CallCounter`** (Resrcify.SharedKernel.IntegrationTesting,
  `Waiting`), from the copies in the services' test projects (Discord's, the newest). A test waits for its condition,
  never for a time: `AwaitableLog<T>.WaitForAsync(matches)` completes as a matching entry is added (at once if one
  is), `CallCounter.ReachedAsync(n)` once `n` calls were made, and `HangGuard.AwaitAsync(signal, "what", diagnostics)`
  awaits one with the only limit, `HangGuard.Limit`, which fails a hung test naming what it waited for (and the
  diagnostics). `HangGuard.UntilAsync(holds, …)` checks state every 250 ms, only for state with no signal (another
  process's). The limit is **10 minutes**: the services' copies had 5 (Sentinel's `TestClock.HangGuard`,
  `SwgohApiPod.HangGuard`) and 10 (Discord, SwgohApi); a hang detector costs a passing run nothing, so the longer one
  is kept, and `limit:` takes a shorter one per wait. New over the copies: the `limit:` and `checkEvery:` parameters
  (a zero limit tests a hang without waiting), and the message names the limit it used.

  Adopting it: a service deletes its `HangGuard.cs`, `AwaitableLog.cs` and `CallCounter.cs` and adds
  `using Resrcify.SharedKernel.IntegrationTesting.Waiting;` (Discord: `IntegrationTests/Fixtures`,
  `Infrastructure.UnitTests/TestDoubles`; Sentinel: its `HangGuard` constants become `HangGuard.Limit`, or
  `limit: TimeSpan.FromMinutes(5)` to keep its 5).
- **`ImageUnderTest` and `ImageAcquisition`: another service's image, and exactly which build ran**
  (Resrcify.SharedKernel.IntegrationTesting, `Containers`), from the identical copies in the services' system and
  end-to-end tests. `StartAsync(configure)` starts a container of the image: the run's first one pulls it fresh
  (`PullFresh`: Testcontainers' `PullPolicy.Always`, so a stale `latest` never stands in for a newer release, and a
  failed pull fails the run naming the image, its registry and how to pick another) or uses the local copy
  (`LocalOnly`); a reference pinned by digest is used as it is. From then on the run names the image by its registry
  digest, and `Description` — written once to the console as a `[tested against]` line — gives its version and
  revision labels, digest, image id and creation time. Generic now: no service's image or registry in it (the
  failed-pull message names the reference's own registry). Adopting it: a service deletes its
  `Support/ImageUnderTest.cs` (`Fixtures/` in Discord) and adds `using Resrcify.SharedKernel.IntegrationTesting.Containers;`.
- **`ServiceHostFixture.RenderedLogs`: what the host logged through Serilog, as production's console prints it**
  (Resrcify.SharedKernel.IntegrationTesting, `Logging`: `ProductionRenderedLogs`, `ProductionRenderedLine`), from the
  identical copies in Shard, Sentinel, Oracle and Tournament. Each line's level, template and message as
  `RenderedCompactJsonFormatter` renders `@m` — a string property quoted (`failed "0"`) unless the template says `:l`,
  which `Logs` never shows. Registered in the container as an `ILogEventSink` beside `Logs`' sink, so a service whose
  Serilog reads the container's sinks (`AddServiceTelemetry`) feeds both; `Clear()` between tests. The package now
  references Serilog.Formatting.Compact 3.0.0. Adopting it: a service deletes its `ProductionRenderedLogs.cs` and
  its registration in `ConfigureTestServices`, and reads `fixture.RenderedLogs.Lines`.
- **`ConventionalConfigurationTests`: no type takes `IConfiguration` in a constructor**
  (Resrcify.SharedKernel.ArchitectureTesting), from SwgohApi's `ConfigurationTests`, in the style of the other
  conventional bases (`BaseArchitectureTest`'s layer assemblies, `[SkippableFact]`, virtual, a constructor taking the
  assemblies). `Types_Should_NotTakeIConfigurationInAConstructor` checks every class of every layer assembly but the
  generated `Program`: a constructor of any visibility with a parameter of `IConfiguration` or anything implementing it
  (`IConfigurationSection`, …) fails, named by layer, type and parameter. **Constructors only**: a registration
  extension method taking `IConfiguration` (`AddInfrastructure(this IServiceCollection, IConfiguration)`) is where
  configuration is read, and passes. Matched by full name, so the package takes no configuration dependency. Adopting
  it: a service adds `public sealed class ConfigurationTests : ConventionalConfigurationTests;` (SwgohApi replaces its
  own `ConfigurationTests`); a class that takes `IConfiguration` moves to `IOptions<T>` bound at registration.
- **`AddServiceApiVersioning` and `ApiVersioning`: the API versioning every service set up** (Resrcify.SharedKernel.Web,
  `Versioning`). `services.AddServiceApiVersioning(versionReader, configureOpenApi)` is the
  `AddApiVersioning(...).AddApiExplorer(...).AddOpenApi()` block of each service's `InfrastructureServiceRegistration`
  (versions reported, 1.0 assumed when unspecified, `'v'VVV` groups with the version substituted in the URL, an
  OpenAPI document per version); `versionReader` keeps each service's wire behaviour: left out, Asp.Versioning's
  default (Shard, Oracle); `new MediaTypeApiVersionReader("api-version")` for Sentinel, Tournament and SwgohApi.
  `configureOpenApi` takes SwgohApi's schema transformer. `ApiVersioning.VersionSet` / `app.CreateApiVersionSet()` is
  the identical static class of each service's `Presentation/Endpoints/ApiVersioning.cs`. The package now references
  Asp.Versioning.Http 10.2.3, Asp.Versioning.Mvc.ApiExplorer 10.2.1 and Asp.Versioning.OpenApi 10.2.3 (the versions
  the services pin). Adopting it: a service deletes its `ApiVersioning.cs`, replaces the block with
  `services.AddServiceApiVersioning()` (or with its reader), and adds `using Resrcify.SharedKernel.Web.Versioning;`
  where it calls `CreateApiVersionSet()` or names `ApiVersioning.VersionSet`.
- **`AddServiceCors(policyName)`: the CORS policy every service defined** (Resrcify.SharedKernel.Web, `Extensions`):
  any origin, header and method, without credentials, under the service's name for it (`ShardCors`, `SentinelCors`,
  `OracleCors`, `TournamentCors`, `SwgohApi`), applied as before with `app.UseCors(name)`. The S5122 suppression and its
  reasoning (bearer tokens guard the routes; `AllowAnyOrigin` excludes credentials) move with it. Adopting it: a
  service replaces its `services.AddCors(...)` block and `#pragma warning disable S5122` in
  `PresentationServiceRegistration` with `services.AddServiceCors("<its name>")`.

## 4.6.0 (2026-10-19)

### Added

- **`INonConcurrentRequest`: requests that run one at a time** (Resrcify.SharedKernel.Abstractions, `Mediator`, and
  Resrcify.SharedKernel.Mediator). A request implementing it is handled one at a time per request type and
  `string? ConcurrencyKey` (a default member: `null`): while one is handled, another of the same type and key waits
  until it has finished, whatever its end (a result, a failure, an exception), and is then handled itself.
  `null` or empty is one lock for the whole request type; a key narrows it (a shard's ID: different shards side by
  side). It **serialises, it doesn't coalesce** (the run that waited gets its own result, not the first one's: that is
  `IIdempotentRequest`'s); it holds **per process**, not across replicas; it **waits rather than refuses**, with
  nothing timing out on its own; cancelled while waiting (the send's `CancellationToken`), the request isn't handled
  and the send throws `OperationCanceledException`. It isn't re-entrant: a handler that sends and awaits a request of
  its own type and key waits for itself. **`NonConcurrentPipelineBehavior<TRequest, TResponse>`**
  (`Mediator.Behaviors`; `TRequest : INonConcurrentRequest`, `TResponse : Result`, so the container closes it for no
  other request) holds the lock around the rest of the pipeline; the locks are the mediator's (a singleton per
  container, registered by `AddMediator`), and a lock is kept only while a run holds it or waits for it, so keys can
  be as many as the data makes. The lock stays internal: nothing outside the pipeline needs to share it.
- **`StandardBehavior.NonConcurrent`, in the standard set right after validation**: Logging → Idempotency →
  Validation → **NonConcurrent** → Transaction → UnitOfWork → Caching. After validation, so an invalid request is
  refused without waiting; outside the transaction and the unit of work, so the next run starts once the previous one
  committed and sees what it saved (and waits without holding a connection); outside caching, so a query that waited
  is answered from what the first one cached instead of missing the cache and running again. Leave it out with
  `.Without(StandardBehavior.NonConcurrent)`. Like the other request markers, the behavior check
  (`AddMediator`) refuses a scanned `INonConcurrentRequest` that no registered behavior handles, rather than letting
  it run concurrently unnoticed. `StandardBehavior.NonConcurrent` is declared last (value 6): order by name.

  Adopting it:

  - **SwgohApi** replaces its `ISingleFlightRequest` (same semantics: serialisation, per process): in
    `ApplicationServiceRegistration`, `config.AddOpenBehavior(typeof(SingleFlightPipelineBehavior<,>), ServiceLifetime.Singleton)`
    becomes `config.AddOpenBehavior(typeof(NonConcurrentPipelineBehavior<,>))`, in the same place (its behaviors are
    added by hand, not as the standard set); `GetGameDataRequest` implements `INonConcurrentRequest` instead and drops
    `SingleFlightKey => nameof(GetGameDataRequest)` (no key is one lock for the request type, as that constant was);
    `ISingleFlightRequest`, `SingleFlightPipelineBehavior` and `SingleFlightPipelineBehaviorTests` go.
    `ClientVersionUpdatorPipelineBehavior` keeps a lock of its own (its `"ClientVersion"` key was never a request's,
    so it never shared one with the pipeline): one `SemaphoreSlim` in a singleton of SwgohApi's in place of
    `ISingleFlightLockProvider`.
  - **Titan services** mark their job commands `INonConcurrentRequest` (with a key where runs for different targets
    may overlap), so a scheduled Quartz run and an on-demand `POST api/Jobs/run` of the same job never overlap within
    an instance. Services on `AddStandardBehaviors()` need nothing else; across replicas, a job still needs Quartz's
    clustering or a lock in the database.

## 4.5.0 (2026-10-18)

### Added

- **`app.UseServiceDefaults(Action<RequestLoggingOptions>? configureRequestLogging = null)`**
  (Resrcify.SharedKernel.Observability, `Extensions`, next to `UseServiceRequestLogging`): the start of a service's
  pipeline in one call, in the order it needs: `UseServiceRequestLogging(configureRequestLogging)`, then
  `UseExceptionHandler()`. The request logging wraps the exception handler, so a request's line carries the status the
  client got (the handler's 500, a client abort's 499 at `Information`) and an exception's stack is logged once, by
  the handler; every service wrote the two lines by hand, in an order only the README held it to. The exception
  handler is ASP.NET Core's and answers with what the service registers (`services.AddResultProblemDetails()`, Web),
  so Observability still references no other SharedKernel package but Results, and Web none of Observability's. The
  endpoints (`MapHealthEndpoints()`, `MapServiceMetrics()`) stay separate calls: an endpoint's place in `Program.cs`
  changes nothing, each keeps its options and its returned builder, and the health endpoints are Web's. The service's
  own middleware (HTTPS redirection, status code pages, routing, CORS, authentication) goes after it. Each service
  replaces its two lines, and the comments above them, in `src/<Service>.Web/Program.cs`:

  | Service | Was | Now |
  |---|---|---|
  | Shard, Oracle, SwgohApi | `app.UseServiceRequestLogging();` … `app.UseExceptionHandler();` | `app.UseServiceDefaults();` |
  | Tournament | `app.UseServiceRequestLogging(); app.UseHttpsRedirection(); app.UseExceptionHandler();` | `app.UseServiceDefaults(); app.UseHttpsRedirection();` (the redirection moves inside the handler: harmless) |
  | Sentinel | `app.UseServiceRequestLogging();`, `app.UseDeveloperExceptionPage();` in the `IsDevelopment()` branch and `else { app.UseExceptionHandler(); }` | `app.UseServiceDefaults();` before the `if`, which keeps only OpenAPI and Scalar: in Development an unhandled exception is then answered as everywhere else, problem details whose `detail` holds the exception (`ResultExceptionHandler`'s Development details), instead of the developer exception page |

  `MapHealthEndpoints()`, `MapServiceMetrics()` and `AddResultProblemDetails()` stay as they are.

### Behaviour changes

- **A second request logging on one pipeline throws when the pipeline is built** (Resrcify.SharedKernel.Observability):
  `UseServiceRequestLogging()` or `UseServiceDefaults()` called twice, or the two together (in either order), throws an
  `InvalidOperationException` naming both calls, instead of logging every request twice. The pipeline records what
  added its request logging in `IApplicationBuilder.Properties`; a branch built after it (`UseWhen`, `Map`) inherits
  that. No Titan service calls either twice. A `UseExceptionHandler()` of the service's own next to
  `UseServiceDefaults()` isn't looked for (ASP.NET Core leaves no dependable trace of it on the pipeline) and is
  harmless: the handler nearest the endpoint answers; delete it when moving to the one call.

## 4.4.1 (2026-10-17)

Bug fixes only: no API changes, and nothing for a service to change.

### Fixed

- **A rate-limited queue's bus is stopped once, and none starts after the host stopped** (Resrcify.SharedKernel.MessageBus).
  A test host stops a service twice at the same time: `WebApplicationFactory.DisposeAsync` stops the host while
  `Program.cs`'s `app.RunAsync()` waits for it to stop (`WaitForShutdownAsync`) and then stops it too, and
  `Host.StopAsync` has no guard against a second call, so every hosted service is stopped twice at once (and
  `app.RunAsync()` then disposes the host, maybe while the first stop still runs). The queue's host checked
  `_activator is null` and only cleared it after disposing the bus, which waits for its workers, so the second stop
  passed the check too: the bus was disposed twice and `Stopped consuming <queue>; its messages go back to the other
  instances` logged twice for one queue (seen in SwgohApi's system tests). The running bus (its activator, its
  cancellation and its rate limiter) is now taken out of its slot in one atomic step by whoever stops it (the health
  gate's loop, `StopAsync`, `Dispose`, several at once), so it is disposed once and the line logged once. Also fixed:
  a bus that was starting when the host stopped without waiting for the queue's loop (`Dispose`, or `StopAsync` past
  the host's shutdown timeout, while the health gate restarted the queue) kept running, unowned, after the stop; it is
  now stopped as soon as it has started, and never counts as consuming. A single stop (production's `SIGTERM`) behaves
  as before.
- **Each host logs through its own Serilog logger** (Resrcify.SharedKernel.Observability). `AddServiceTelemetry` called
  Serilog's `AddSerilog` with its default `preserveStaticLogger: false`: the host set the static `Log.Logger` to its
  logger, every `ILogger` it made wrote through the `Log.Logger` of the moment it was made, and its stop closed
  `Log.Logger` (`Log.CloseAndFlush`). In a process with two hosts (a test's), a logger the first host made after the
  second was built (a request handler's, made at its first request) wrote as the second, and the first host's stop
  closed the second's logger. Now each host's logger factory writes to the host's own logger and its stop disposes
  that logger only, whatever the other hosts do. **The static `Log.Logger` is still set**, for code logging through
  Serilog's static API: to the first host's logger while it runs (the only host, in a service, so exactly as before),
  and back to `Logger.None` when it stops (as `Log.CloseAndFlush` left it) unless something else replaced it
  meanwhile; a host built after that takes it then. No Titan service logs through the static API today. SwgohApi's
  system tests can drop their workaround (`SwgohApiInstance.Factory.LogThroughThisHostsLogger`, which takes the host's
  loggers off `Log.Logger`), and the reason `SwgohApiHostCollection` gives for running its host tests one at a time
  (the static `Log.Logger`) is gone; both still work unchanged.

## 4.4.0 (2026-10-16)

### Added

- **A rate-limited queue's options are named options** (Resrcify.SharedKernel.MessageBus): every `AddRateLimitedQueue`
  overload (the `THandler` ones, with and without a queue name, and the mediator-forwarding one) registers
  `services.AddOptions<RateLimitedQueueOptions>(queueName)`, its `configure` becoming that instance's first
  `Configure`, and the queue's host reads `IOptionsMonitor<RateLimitedQueueOptions>.Get(queueName)` when the host
  starts instead of taking an instance built at registration. A service can now configure a queue with the options
  pattern, from its own typed options and with its rate limiter from the container, with no `IConfiguration` at
  registration. What it registers **after** `AddMessageBus` runs after the registration's `configure` and wins (what it
  registers before loses to it; `PostConfigure` runs last). **`RateLimitedQueueOptions.NameFor<TRequest>()`** names a
  queue (the request's class name, as `AddRateLimitedQueue` names it). Two queues given the same
  `SharedRateLimiterStrategy` instance still share one limiter, also when it comes from a singleton in the container.
  SwgohApi can drop `configuration.GetSection(GameRateLimits.SectionName).Get<GameRateLimits>()` and the
  `GetSection("MessageBus:Queues:<name>").Bind(queue)` in `ScatterRequestServiceRegistration`:

  ```csharp
  services.AddOptions<GameQueues>()
      .BindConfiguration("GalaxyOfHeroes:Queues", binder => binder.ErrorOnUnknownConfiguration = true)
      .ValidateOnStart();
  services.AddSingleton<GameBudgets>();   // IOptions<GameRateLimits> → one SharedRateLimiterStrategy per budget
  services.AddMessageBus(bus => bus
      .AddRateLimitedQueue<GuildPayload, GetGuildResponse>(payload => new GetGuildRequest { Payload = payload }));
  services.AddOptions<RateLimitedQueueOptions>(RateLimitedQueueOptions.NameFor<GuildPayload>())
      .Configure<IOptions<GameQueues>, GameBudgets>((queue, queues, budgets) =>
      {
          queue.RateLimiter = budgets.Authenticated;
          queue.Prefetch = queues.Value.Guild.Prefetch;
      });
  ```

  Existing registrations behave as before: their `configure` sets the same values, now when the host starts.

### Behaviour changes

- **A rate-limited queue's options are validated when the host starts** (`ValidateOnStart`, one validator per queue):
  `PerSecond` at least 1, `Burst` not negative, `Prefetch` unset or at least 1, a positive `HealthCheckInterval` (at
  most 49.7 days, what `Task.Delay` takes), a non-empty `HealthCheckTag` when set, a `RateLimiter` and an
  `AnswerFailure`, and `Retries`' `FirstDelay` / `MaxDelay` within 49.7 days. A bad value fails the start with an
  `OptionsValidationException` naming the queue and each wrong key (`The rate-limited queue GetGuildRequest's PerSecond
  must be at least 1; it is 0.`). There was no validation: `PerSecond = 0` failed every start of the queue's bus (the
  token bucket refused it), logged and retried forever while the host ran, and a zero `HealthCheckInterval` spun the
  health gate.
- **A queue's `configure` runs when the host starts, not inside `AddRateLimitedQueue`.** A `configure` that reads a
  variable sees its value at start; one that throws fails the start instead of the registration.

## 4.3.0 (2026-10-16)

### Breaking changes

- **One `RetrySchedule` value type and one backoff function** (Resrcify.SharedKernel.Abstractions, `Resilience`):
  `public sealed record RetrySchedule(int MaxAttempts, TimeSpan FirstDelay, TimeSpan MaxDelay, bool Jitter = false)`
  with `DelayAfter(int failedAttempt)` (the first delay doubled per try before it, capped at `MaxDelay`,
  overflow-safe; with `Jitter`, spread between half of it and all of it) and `HasAttemptAfter(int)`. **Every count is
  attempts, the first one included.** It replaces the options of every retrying package; the defaults are unchanged:

  | Was | Now |
  |---|---|
  | `OutboxOptions.MaxRetryCount` (3), `RetryDelay` (5 s), `MaxRetryDelay` (5 min) | `OutboxOptions.Retries = new RetrySchedule(3, 5 s, 5 min)` |
  | `OutboxOptions.MaxTransientRetryCount` (`int?`) | `OutboxOptions.TransientRetries` (`RetrySchedule?`; unset: `Retries` with the higher of 12 and its attempts) |
  | `OutboxLaneOptions.MaxRetryCount`, `MaxTransientRetryCount`, `RetryDelay`, `MaxRetryDelay` | `OutboxLaneOptions.Retries`, `TransientRetries` (as above) |
  | `AddProcessOutboxMessagesJob(..., int processMaxRetryCount = 3, ..., int? processMaxTransientRetryCount = null)` | `AddProcessOutboxMessagesJob(..., RetrySchedule? retries = null, ..., RetrySchedule? transientRetries = null)` (same positions) |
  | `ResultResilienceOptions.MaxRetries` (3 **retries**), `RetryDelay` (1 s), `MaxRetryDelay` (30 s), `UseJitter` (on) | `ResultResilienceOptions.Retries = new RetrySchedule(4 attempts, 1 s, 30 s, Jitter: true)`: `MaxRetries = n` is `MaxAttempts = n + 1`; `MaxAttempts = 1` turns retrying off |
  | the bus' event tries (Rebus' `MaxDeliveryAttempts`, 5), 0.5 s doubling to 4 s, fixed | `MessageBusBuilder.WithEventRetries(RetrySchedule)` (unset: 5 attempts, or the retry strategy's delivery attempts, 0.5 s doubling to 4 s) |
  | a rate-limited queue's tries (Rebus' `MaxDeliveryAttempts`, 5), 0.5 s × 2ⁿ, **uncapped** | `RateLimitedQueueOptions.Retries` (`RetrySchedule?`; unset: as the events, capped at 4 s) |

  A `MaxRetryDelay` shorter than `RetryDelay` threw at registration; a `MaxDelay` shorter than `FirstDelay` now caps
  every delay at it. A `RetrySchedule` with fewer than 1 attempt or a negative delay throws as it is made (also with
  `with`). The hand-written calculators are gone (`OutboxRetryBackoff`, the dispatcher's and the queue host's
  `RetryDelay`, the queue host's start retry, scatter-gather's restart, `ConcurrentCreationRetry`'s, the PostgreSQL
  outbox listener's reconnect): they all wait by `RetrySchedule.DelayAfter`, with the same delays.
  **Fixed with it: a rate-limited queue's wait between tries was uncapped** (500 ms × 2ⁿ⁻¹), so a configuration
  strategy raising the delivery attempts to about 24 made `Task.Delay` throw (and the waits held the request
  unacknowledged for minutes before that): it is capped at 4 s, as the events'. Tournament, Shard, Oracle, Discord and
  Sentinel set `MaxRetries` / `RetryDelay` / `MaxRetryDelay` on `AddResultResilience`: `resilience.Retries = new
  RetrySchedule(MaxAttempts: 4, FirstDelay: TimeSpan.FromSeconds(1), MaxDelay: TimeSpan.FromSeconds(4), Jitter: true)`
  for `MaxRetries = 3, RetryDelay = 1 s, MaxRetryDelay = 4 s`; Sentinel's lane sets `lanes.Retries` /
  `TransientRetries` instead of `MaxRetryCount`.

- **One error-code catalogue: `ErrorCodes`** (Resrcify.SharedKernel.Results, `Primitives`; see Added). The constants
  it replaces are gone: **`ErrorTypeExtensions.ConcurrencyConflictCode`** (use `ErrorCodes.Persistence.Concurrency`,
  the same `"Persistence.Concurrency"`) and **`ResultExceptionHandler.ErrorCode`** (Web; use
  `ErrorCodes.Request.Unhandled`, the same `"Unhandled"`). The codes themselves are unchanged. A service matching a
  SharedKernel code with a string of its own (Discord's `ErrorMessageExtensions.ResilienceTimeoutCode` /
  `CircuitOpenCode`, Tournament's test stubs) can use the catalogue's constant instead.

- **One mediator runtime: the pipeline the container composes** (Resrcify.SharedKernel.Mediator). The mediator had two
  ways to build a request's pipeline, chosen by `cfg.EnableDiTimePipelineComposition()`: every Titan service turned on
  the container-composed one, while `AddMediator(params Assembly[])` and `AddMediator(ServiceLifetime, params
  Assembly[])` silently used the other. Now there is one: a request's pipeline (`DiComposedSendRuntime<,>` /
  `DiComposedStreamRuntime<,>`, transient) is resolved from the container, which builds the handler, behaviors and
  processors with their own lifetimes (transient unless registered otherwise), and behaviors run in the order added,
  as before. The mediator stays transient by default (`cfg.UseMediatorLifetime(ServiceLifetime.Scoped)` as before).
  Removed:

  | Was | Now |
  |---|---|
  | `cfg.EnableDiTimePipelineComposition()` / `EnableDiTimePipelineComposition(bool)` | delete the call: it is the only runtime |
  | `services.AddMediator(params Assembly[] assemblies)` | `services.AddMediator(cfg => cfg.RegisterServicesFromAssemblies(assemblies))` |
  | `services.AddMediator(ServiceLifetime lifetime, params Assembly[] assemblies)` | `services.AddMediator(cfg => cfg.RegisterServicesFromAssemblies(assemblies).UseMediatorLifetime(lifetime))` |

  Every Titan service (Sentinel, Tournament, Shard, Oracle, Discord, SwgohApi, Sandbox, Identity) deletes its
  `config.EnableDiTimePipelineComposition();` line in `Application/ApplicationServiceRegistration.cs`; nothing else.

- **One clock path: the container's `TimeProvider`, and only it** (Resrcify.SharedKernel.UnitOfWork,
  UnitOfWork.Postgres, Mediator, MessageBus, Caching, Web). The clock was found four ways: from the container when
  used, the one registered *before* `AddOutboxProcessing` (captured at registration, so a `FakeTimeProvider` added
  later in `ConfigureTestServices` was silently ignored for the jobs' first runs), `OutboxOptions.TimeProvider`, and
  `timeProvider` parameters defaulting to the system clock. Now every SharedKernel type reads the container's, resolved
  when it is used (a job trigger's first run when the scheduler is built), and every registration
  (`AddOutboxProcessing`, `AddProcessOutboxMessagesJob`, `AddCleanupOutboxMessagesJob`, `AddOutboxLanes`, the interval
  and cron job helpers, `AddJobHeartbeats`, `AddEntityInterceptors` / `AddOutboxInterceptor`, `AddMigrationsOnStartup`,
  `AddMediator`, `AddMessageBus`, `AddResultResilience`) makes `services.TryAddSingleton(TimeProvider.System)`, so a
  clock registered before or after it wins. Removed or changed:

  | Was | Now |
  |---|---|
  | `OutboxOptions.TimeProvider` | removed: register the clock in the container |
  | `AddIntervalJob` / `AddIntervalCommandJob` (4 overloads) / `AddCronJob` / `AddCronCommandJob` (4 overloads)`(..., TimeProvider? timeProvider = null)` | the parameter is removed |
  | `AddProcessOutboxMessagesJob(..., IOutboxLaneClaim? claim, TimeProvider? timeProvider, RetrySchedule? transientRetries)` | `(..., IOutboxLaneClaim? claim, RetrySchedule? transientRetries)` |
  | `AddCleanupOutboxMessagesJob(int processedRetentionInDays, TimeProvider? timeProvider = null)` | `(int processedRetentionInDays)` |
  | `new InsertOutboxMessagesInterceptor(serializer, insertStrategy?, timeProvider?, observers?, logger?)` | `new InsertOutboxMessagesInterceptor(serializer, timeProvider, insertStrategy?, observers?, logger?)`: the clock is required and second |
  | `new UpdateAuditableEntitiesInterceptor(TimeProvider? = null)`, `new UpdateDeletableEntitiesInterceptor(TimeProvider? = null)` | the clock is required |
  | `ProcessOutboxMessagesJob(scopeFactory, TimeProvider? = null, wakeUp?)`, `CleanupOutboxMessagesJob(scopeFactory, logger, TimeProvider? = null)`, `JobHeartbeatRegistry(TimeProvider? = null)` | the clock is required (the container passes it) |
  | `LoggingPipelineBehavior(logger, TimeProvider? = null, options?, transientFailures?)` (Mediator), `DistributedCachingService(cache, TimeProvider? = null)` (Caching) | the clock is required (the container passes it) |

  The internal hosted services (outbox lanes, backlog monitor, migrations, the PostgreSQL outbox listener) and the
  message bus' receive order take it the same way. A service's own `services.TryAddSingleton(TimeProvider.System)` is
  now redundant wherever it registers any of these (Sentinel and Oracle in `Application/ApplicationServiceRegistration.cs`,
  Discord in `Application/ApplicationServiceRegistration.cs`, Tournament, Shard and SwgohApi in
  `Infrastructure/InfrastructureServiceRegistration.cs`, Sandbox in `Infrastructure/InfrastructureServiceRegistration.cs`):
  delete it, or keep it as documentation; it does no harm. A service that registers `DistributedCachingService`
  itself without any SharedKernel registration making the clock must register one (or use `AddDistributedCaching`,
  see Added). **Sandbox and Identity** build the interceptors with `new` in `PersistenceServiceRegistration.cs` and their
  design-time factories: pass the clock (`new InsertOutboxMessagesInterceptor(serializer, sp.GetRequiredService<TimeProvider>(),
  new PostgresOnConflictOutboxInsertStrategy())`, `new UpdateAuditableEntitiesInterceptor(sp.GetRequiredService<TimeProvider>())`;
  `TimeProvider.System` in a design-time factory), or register them with `AddEntityInterceptors().AddOutboxInterceptor()`
  and `options.AddSaveChangesInterceptors(provider)`. No Titan service passed a `timeProvider` argument or set
  `OutboxOptions.TimeProvider`.

- **`HealthTags` moved to Abstractions** (from Resrcify.SharedKernel.Web): `Resrcify.SharedKernel.Web.Health.HealthTags`
  is now `Resrcify.SharedKernel.Abstractions.Health.HealthTags`, the same `Ready` / `Live` constants, so every package
  registering a check shares it. A service using it changes `using Resrcify.SharedKernel.Web.Health;` to
  `using Resrcify.SharedKernel.Abstractions.Health;` where it names `HealthTags` (Oracle, Sentinel, Shard and Tournament
  in `Infrastructure/InfrastructureServiceRegistration.cs`; keep the Web one where `HealthEndpointOptions` is used).

- **The container fixtures take their settings as init properties; a subclass' members of those names clash**
  (Resrcify.SharedKernel.IntegrationTesting). `PostgresContainerFixture` has `Image`, `Database`, `Username` and
  `Password`, `RabbitMqContainerFixture` `Image`, `Username`, `Password` and `ProductionQueueDefaults` (see Added). ⚠️
  A subclass declaring members of those names (the services' `PostgresFixture` / `RabbitMqFixture` with `const string
  Username` / `Password` / `Database`) hides them: warning CS0108, an error under `TreatWarningsAsErrors`. Delete the
  subclass (what the init properties are for) or rename its constants. The package now references
  Resrcify.SharedKernel.MessageBus.Testing (so MessageBus, Rebus and Rebus.RabbitMq come with it),
  Microsoft.Extensions.Diagnostics.Testing and Serilog.

- **The outbox's intervals and retention are `TimeSpan`s** (Resrcify.SharedKernel.UnitOfWork), as
  `OutboxLaneOptions.PollInterval` already was. The defaults are unchanged:

  | Was | Now |
  |---|---|
  | `OutboxOptions.ProcessIntervalInSeconds` (60) | `OutboxOptions.ProcessInterval` (`TimeSpan.FromSeconds(60)`; at least 1 s) |
  | `OutboxOptions.DelayInSecondsBeforeStart` (60) | `OutboxOptions.DelayBeforeStart` (`TimeSpan.FromSeconds(60)`) |
  | `OutboxOptions.ProcessedRetentionInDays` (7; 0 turns the cleanup off) | `OutboxOptions.ProcessedRetention` (`TimeSpan.FromDays(7)`; `TimeSpan.Zero` turns it off) |
  | `OutboxOptions.BacklogCheckIntervalInSeconds` (30) | `OutboxOptions.BacklogCheckInterval` (`TimeSpan.FromSeconds(30)`; at least 1 s) |
  | `AddProcessOutboxMessagesJob(..., int processIntervalInSeconds = 60, int delayInSecondsBeforeStart = 60, ..., int processedRetentionInDays = 7, ...)` | `AddProcessOutboxMessagesJob(..., TimeSpan? processInterval = null, TimeSpan? delayBeforeStart = null, ..., TimeSpan? processedRetention = null, ...)` (same positions; `null`: the defaults) |
  | `AddCleanupOutboxMessagesJob(int processedRetentionInDays = 7)` | `AddCleanupOutboxMessagesJob(TimeSpan? processedRetention = null)` (positive) |

  `AddProcessOutboxMessagesJob` now refuses an interval under 1 s or a negative delay or retention
  (`ArgumentOutOfRangeException`), as `AddOutboxProcessing` did. The jobs' data stores
  `ProcessIntervalInMilliseconds` and `ProcessedRetentionInMilliseconds` (longs); a job stored before 4.3.0 in a
  persistent Quartz store is still read from `ProcessIntervalInSeconds` / `ProcessedRetentionInDays`. The cleanup's
  log line names the retention as a `TimeSpan` (`older than 7.00:00:00`). Sentinel, Shard and Tournament write
  `outbox.ProcessInterval = TimeSpan.FromSeconds(20)` for `outbox.ProcessIntervalInSeconds = 20`
  (`Infrastructure/InfrastructureServiceRegistration.cs`).

- **The packages a SharedKernel package's API is built on are its dependencies, no longer private**
  (Resrcify.SharedKernel.Observability, Web, UnitOfWork, UnitOfWork.Postgres, Repository, Mediator, Caching). Their
  `PackageReference`s were `PrivateAssets="all"`, though their types are in the packages' API (`TracerProviderBuilder`,
  Serilog's `LoggerConfiguration` and `RequestLoggingOptions`, `IHttpResiliencePipelineBuilder`, `JwtBearerOptions`,
  EF Core's `DbContext`, Quartz's `IJob`, Npgsql's EF builder, FluentValidation's `IValidator<T>`, `IDistributedCache`):
  every service listed 12–14 of them itself, in lockstep, and one at a lower version than SharedKernel was built
  against failed at run time with a `MissingMethodException` (or a `FileNotFoundException`, for one it didn't list),
  not at restore. They are now ordinary dependencies, in each package's `.nuspec`, so NuGet brings them and refuses a
  lower version at restore (`NU1605`, a downgrade, is an error):

  | Package | Now depends on (at least) |
  |---|---|
  | Observability | `OpenTelemetry.Extensions.Hosting` 1.19.1, `OpenTelemetry.Instrumentation.AspNetCore` / `.Http` / `.Runtime` 1.19.0, `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.19.1, `Serilog.AspNetCore` 10.0.0, `Serilog.Extensions.Hosting` 10.0.0, `Serilog.Settings.Configuration` 10.0.1 |
  | Web | `Microsoft.Extensions.Http.Resilience` 10.11.0 (and with it `Microsoft.Extensions.Http`), `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.13 |
  | UnitOfWork | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.Diagnostics.HealthChecks` 10.0.13, `Quartz` 4.5.0 |
  | UnitOfWork.Postgres | UnitOfWork's, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (and Npgsql), `Microsoft.Extensions.Hosting` 10.0.13 |
  | Repository | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational` 10.0.13 |
  | Mediator | `FluentValidation`, `FluentValidation.DependencyInjectionExtensions` 12.2.0, `Microsoft.Extensions.Logging` 10.0.13 |
  | Caching | `Microsoft.Extensions.Caching.Abstractions`, `Microsoft.Extensions.Caching.Memory` 10.0.13 |

  Still private: SonarAnalyzer (build only), and `OpenTelemetry.Exporter.Prometheus.AspNetCore` (Observability), only
  ever published as a prerelease, which a stable package can't depend on (`NU5104`): the service keeps referencing it.
  Abstractions, Results and DomainDrivenDesign, which Domain references, have no such dependency (Abstractions'
  `Microsoft.Extensions.DependencyInjection.Abstractions` was public already). **Mediator and Caching were kept private
  on purpose before; not any more**: Mediator's `ValidationPipelineBehavior` takes `IValidator<T>` and its assembly scan
  (4.3.0, see Behaviour changes) calls FluentValidation's, so a service deleting its own
  `FluentValidation.DependencyInjectionExtensions` along with `AddValidatorsFromAssembly` would have failed at start-up;
  Mediator is referenced by Application (where the validators are), never by Domain. Caching is used by
  Infrastructure only (inner layers take `ICachingService` from Abstractions), so nothing reaches Domain or
  Application through it. ⚠️ **A service pinning one of these lower than SharedKernel fails to restore** (`NU1605`):
  **SwgohApi** pins `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Caching.Abstractions` and
  `Microsoft.Extensions.Diagnostics.HealthChecks` at 10.0.12 (`Directory.Packages.props`): raise them to 10.0.13 (or
  delete the references, see below). A service may keep any of these references (to move one higher); none is needed.
  What each service can delete (`PackageReference`s in its `*.csproj`, and their `PackageVersion`s in
  `Directory.Packages.props` once no project references them):
  - **Shard**: `Application`: `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`. `Infrastructure`:
    `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`,
    `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `Serilog.AspNetCore`,
    `Serilog.Extensions.Hosting`, `Serilog.Settings.Configuration`, `Microsoft.Extensions.Http`,
    `Microsoft.Extensions.Http.Resilience`, `Quartz`, `Microsoft.Extensions.Diagnostics.HealthChecks`. `Persistence`:
    `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Npgsql.EntityFrameworkCore.PostgreSQL`.
    `tests/*.IntegrationTests` and `tests/*.SystemTests`: `Microsoft.EntityFrameworkCore`,
    `Microsoft.EntityFrameworkCore.Relational`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Quartz`.
  - **Sentinel**: `Application`: `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`.
    `Infrastructure`: the same twelve as Shard's. `Persistence`: `Microsoft.EntityFrameworkCore`,
    `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.EntityFrameworkCore.Abstractions`,
    `Npgsql.EntityFrameworkCore.PostgreSQL`. `tests/*.IntegrationTests`: the three EF Core / Npgsql ones;
    `tests/*.SystemTests`: those and `Quartz`.
  - **Oracle**: `Application`: `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`. `Infrastructure`:
    the same twelve as Shard's. `Persistence`: `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`,
    `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.Extensions.Hosting`. `tests/*.IntegrationTests`: the three
    EF Core / Npgsql ones.
  - **Tournament**: `Application`: `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`.
    `Infrastructure`: the same twelve as Shard's. `Persistence`: `Microsoft.EntityFrameworkCore`,
    `Microsoft.EntityFrameworkCore.Relational`, `Npgsql.EntityFrameworkCore.PostgreSQL`. `tests/*.IntegrationTests`:
    the three EF Core / Npgsql ones.
  - **Discord**: `Application`: `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`,
    `Microsoft.Extensions.Logging`. `Infrastructure`: the five `OpenTelemetry.*` above, `Serilog.Settings.Configuration`,
    `Serilog.Extensions.Logging`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience`,
    `Microsoft.Extensions.Diagnostics.HealthChecks`. `Bot`: `Serilog.Extensions.Hosting`,
    `Serilog.Settings.Configuration` (keep `Serilog`: it pins 4.4.0, above SharedKernel's).
  - **SwgohApi**: `Application`: `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`,
    `Microsoft.Extensions.Logging`. `Infrastructure`: `Microsoft.Extensions.Diagnostics.HealthChecks`; once it moves to
    the Observability package, its `OpenTelemetry.*` (but the Prometheus exporter and
    `OpenTelemetry.Instrumentation.StackExchangeRedis`) and `Serilog.AspNetCore`.

  Every service keeps the Prometheus exporter, its Serilog sinks, formatters and enrichers (`Serilog.Sinks.Console`,
  `Serilog.Formatting.Compact`, `Serilog.Enrichers.Environment`), `Microsoft.EntityFrameworkCore.Design` / `.Tools` /
  `.Analyzers` (never dependencies), and `Microsoft.Extensions.Hosting` / `Microsoft.Extensions.Caching.Abstractions`
  in Application (no package it references there brings them).

- **The outbox's PostgreSQL pieces moved to UnitOfWork.Postgres** (from Resrcify.SharedKernel.UnitOfWork), where
  PostgreSQL-specific code belongs: `PostgresOutboxLaneClaim`, `PostgresOutboxIndexes` and
  `PostgresOnConflictOutboxInsertStrategy`, unchanged, are now in Resrcify.SharedKernel.UnitOfWork.Postgres, namespace
  `Resrcify.SharedKernel.UnitOfWork.Postgres.Outbox` (was `Resrcify.SharedKernel.UnitOfWork.Outbox`). UnitOfWork
  holds no PostgreSQL code (it never referenced Npgsql). A project naming one of them references
  `Resrcify.SharedKernel.UnitOfWork.Postgres` and adds `using Resrcify.SharedKernel.UnitOfWork.Postgres.Outbox;` (keep
  `using Resrcify.SharedKernel.UnitOfWork.Outbox;` where it names `OutboxMessage` or the serializer):
  - **Shard**: `Persistence/ShardDbContext.cs` (`PostgresOutboxIndexes`); `Infrastructure/InfrastructureServiceRegistration.cs`
    line `outbox.Claim = PostgresOutboxLaneClaim.Instance;`: delete it (the default since 4.3.0, see Behaviour changes),
    or add the `using`.
  - **Sentinel**: `Persistence/SentinelDbContext.cs` (`PostgresOutboxIndexes`); `Infrastructure/InfrastructureServiceRegistration.cs`,
    the same `outbox.Claim` line as Shard's.
  - **Tournament**: `Persistence/TournamentDbContext.cs` (`PostgresOutboxIndexes`).
  - **Sandbox** (`Persistence/PersistenceServiceRegistration.cs`, `Persistence/SandboxDbContextFactory.cs`) and
    **Identity** (`Persistence/PersistenceServiceRegistration.cs`, `Persistence/IdentityAppContextFactory.cs`) build
    `new PostgresOnConflictOutboxInsertStrategy()`: their Persistence projects reference only UnitOfWork, so they add
    the `Resrcify.SharedKernel.UnitOfWork.Postgres` `PackageReference` (or move to `AddPostgresDbContext(..., db =>
    db.WithOutbox(outbox => outbox.OnConflictDoNothing = true))`).
  - Oracle, Discord and SwgohApi name none of them.

- **Resrcify.SharedKernel.UnitOfWork.Web is retired: the outbox admin endpoints are the Web package's**, next to the
  error queue's, and reach the outbox through a new contract in Abstractions, so Web references neither UnitOfWork nor
  EF Core:

  | Was | Now |
  |---|---|
  | `app.MapOutboxAdministration<TDbContext>(prefix = "/admin/outbox")` (UnitOfWork.Web, `Resrcify.SharedKernel.UnitOfWork.Web.Extensions`; `where TDbContext : DbContext`) | `app.MapOutboxAdministration<TContext>(prefix = "/admin/outbox")` (Web, `Resrcify.SharedKernel.Web.Extensions`; `where TContext : class`): the same routes, names (`Outbox.<context>.Summary` ...), authorization (an authenticated caller; the group returned takes the rest), requests and answers |
  | `OutboxRetried` (`Resrcify.SharedKernel.UnitOfWork.Web.Extensions`) | `Resrcify.SharedKernel.Web.Extensions.OutboxRetried` |
  | `OutboxSummary`, `OutboxEventTypeSummary`, `OutboxMessageSummary`, `OutboxMessageDetails` (`Resrcify.SharedKernel.UnitOfWork.Outbox`) | the same records in `Resrcify.SharedKernel.Abstractions.UnitOfWork` |
  | `OutboxAdministration<TDbContext>` (UnitOfWork), resolved by the endpoints | `IOutboxAdministration<TContext>` (Abstractions, `Resrcify.SharedKernel.Abstractions.UnitOfWork`), which `OutboxAdministration<TDbContext>` implements; the outbox registrations (`AddOutboxProcessing`, `AddProcessOutboxMessagesJob`, `AddOutboxLanes`) register both, scoped, the same instance in a scope (an `IOutboxAdministration<TContext>` the service registered first is kept) |

  `IOutboxAdministration<TContext>`: `GetSummaryAsync`, `ListGivenUpAsync(take = 50, type)`, `FindAsync(id)`,
  `RetryAsync(id)`, `RetryAllGivenUpAsync(type)`, the new `DeleteAsync(id)` and `DeleteAllGivenUpAsync(type)` (see
  Added), `MaxTake` (500) and `ContextType`. A service calling `MapOutboxAdministration` drops its
  `Resrcify.SharedKernel.UnitOfWork.Web` `PackageReference` and `PackageVersion`, and changes
  `using Resrcify.SharedKernel.UnitOfWork.Web.Extensions;` to `using Resrcify.SharedKernel.Web.Extensions;`; the call
  is unchanged. One naming the records adds `using Resrcify.SharedKernel.Abstractions.UnitOfWork;`. **No Titan service
  used UnitOfWork.Web** (Shard, Sentinel, Oracle, Tournament, Discord, SwgohApi, Sandbox and Identity reference
  neither the package nor `MapOutboxAdministration`), so none changes anything; it is no longer published.

### Behaviour changes

- **One transient contract, owned by SharedKernel, with a DI hook** (Resrcify.SharedKernel.Results, Abstractions,
  UnitOfWork, Mediator, MessageBus, Web). Four rules told a transient exception from any other, and disagreed: the
  outbox's matched Polly's exceptions by type name (and missed an isolated circuit) and walked only `InnerException`,
  the Web resilience pipeline had its own, the message bus none, and the mediator's behaviors and the command jobs logged
  every exception at `Error`. **A service's own transient exception wasn't recognised**: Sentinel's
  `TransientFailureException` got the 3 tries of a handler's bug, not the 12 of an outage. Now one rule decides
  everywhere, `TransientFailures.IsTransient` (Results): the exception or any of its causes (`InnerException`, and every
  one of an `AggregateException`'s) is a `TransientFailureException` (Results), a `TimeoutException`, an
  `OperationCanceledException` (a handler's own; a shutdown is still ruled out by the callers first), a `DbException`
  with `IsTransient`, or an `HttpRequestException` without a status or with a transient one (408, 429, 5xx). A package
  or a service adds to it with an `ITransientFailureRule` (Abstractions, `services.AddTransientFailureRule(...)`), which
  can only add transience; the container's `ITransientFailureClassifier` ORs them all. UnitOfWork registers one for a
  concurrency conflict, a serialization failure and a deadlock (with every outbox registration), the Web package one for
  Polly's rejections (`ExecutionRejectedException`: a timeout, a broken or isolated circuit, the rate limiter; by type,
  with `AddResultResilience`), so no package outside Web names Polly's types. What changes:
  - **The outbox** gives a failure its transient tries by the classifier: a `TransientFailureException` (and a
    service's own rule) gets 12 tries, an isolated circuit too.
  - **The mediator's logging, unit-of-work and transaction behaviors and the command jobs** (`CommandJob`,
    `AsyncCommandJob`, the `AddIntervalCommandJob` / `AddCronCommandJob` ones) log a transient exception at `Warning`,
    any other at `Error` as before, once.
  - **The message bus** (an integration event's handler): an exception holding a `TransientFailureException` is a
    failure with that exception's errors, which the retry lines and the error queue's reason now show
    (`SwgohApi.Unreachable (ExternalFailure): ...`, where it showed `TransientFailureException: <message>`). A try that threw is
    logged at `Warning` when the exception is transient and **at `Error` otherwise** (it was `Warning` for every try).
    Every exception is still tried again in place.
  - **A rate-limited queue** answers a responder's `TransientFailureException`, after the last try, with its errors
    instead of `<TRequest>.ResponderFailed`, and logs a transient exception at `Warning`.
  - **`ResultExceptionHandler`** (Web) answers a `TransientFailureException` (itself or among the causes) holding
    errors with the problem details of those errors (a 502 for an upstream's failure, a 504 for a timeout ...), logged at
    `Warning` once, where it answered a 500 logged at `Error`.
  - **`AddResultResilience`** retries (and counts against the circuit) the exceptions the rule calls transient, plus
    Polly's attempt timeout, as before, but never an open circuit, a rate limiter's refusal or a cancellation: also a
    `TimeoutException` now, and no longer an `HttpRequestException` carrying a status that isn't transient (a 4xx).
  - **The Web package reads and answers HTTP statuses with Results' `ErrorTypes`** (`FromHttpStatus` /
    `ToHttpStatus`), the same mapping as before.
  ⚠️ An alert on `Error` logs no longer fires for a transient exception of a command or a job (now `Warning`), and
  fires for a non-transient exception an integration event handler threw on a try that is retried. **Sentinel**: delete
  `Application/Retrying/TransientFailureException.cs` and `TransientFailures.cs`, and use Results'
  (`result.ThrowIfTransient()`, which logs nothing itself: the outbox logs the failed try at `Warning`); its events then
  get 12 tries in the regular job as in its `swgohapi` lane.

- **`AddResultResilience` answers a service it couldn't reach with a failed result** (Resrcify.SharedKernel.Web). A
  call that got no answer after the retries (HttpClient's `HttpRequestException` without a status: a refused or reset
  connection, a name that doesn't resolve) threw the exception to the caller, which ended a whole job or request; the
  pipeline now answers it in the service's place, as it does a timeout or an open circuit: a 502 problem details holding
  `ResultResilienceErrors.Unreachable` (`Http.Unreachable`, `ExternalFailure`, transient, its message naming the
  authority called and the cause: `The service at swgohapi:8080 could not be reached: Connection refused
  (swgohapi:8080)`), marked `Resrcify-Resilience: unreachable`, which `ToResultAsync` reads as that failure.
  `ThrowTimeoutsAndOpenCircuits = true` throws it as before. A failure while HttpClient buffers the body of an answer
  (after the pipeline returned it) still throws. ⚠️ A caller catching `HttpRequestException` around a call through
  `AddResultResilience` gets a failed result instead. **Sentinel** can delete its `SwgohApiUnreachableHandler` (and the
  `SwgohApi.Unreachable` code: match `Http.Unreachable`).

- **`TryCompleteAsync` returns the other constraint violations a caller can answer** (Resrcify.SharedKernel.UnitOfWork),
  and so does a command under `ReturnPersistenceFailures`: a not-null violation (`SQLSTATE 23502`), a check-constraint
  violation (`23514`) and a string longer than its column (`22001`) as a `Validation` error (400), a foreign-key
  violation (`23503`, or `23001` for an `ON DELETE RESTRICT` key, as EF Core's `DeleteBehavior.Restrict` declares it)
  as a `Conflict` (409). They threw a `DbUpdateException`, which a service answered with a 500. As for a unique
  violation, the refused save's pending changes are discarded (added rows untracked, changed or deleted ones put back),
  whether or not a transaction is open. ⚠️ A caller catching `DbUpdateException` around `TryCompleteAsync` for these
  gets a failed `Result` instead; `CompleteAsync` still throws.

- **`UseServiceRequestLogging` logs a client abort as a 499 at `Information`, wherever it is placed**
  (Resrcify.SharedKernel.Observability). Placed after `UseExceptionHandler()`, as its documentation said, Serilog's
  middleware saw a request the client gave up on (an `OperationCanceledException` once `RequestAborted` is cancelled)
  and logged it as "responded 500" at `Error` (the 500 is hardcoded in Serilog), though the exception handler answered
  499 at `Debug`: Oracle's Discord requests, closed after 5 s, logged as server errors. Its own branch now answers such
  an exception (or an `IOException`, as `UseExceptionHandler()` tells a client abort) with a 499 before Serilog sees
  it, so the line is a 499 at `Information` with no exception attached, and the default level (`LevelOf`) is
  `Information` for that case too, where it was `Error` for any exception. A real exception still logs as a 500 at
  `Error`.

- **`UseServiceRequestLogging` logs an exception's stack once** (Resrcify.SharedKernel.Observability), following
  SharedKernel's `LoggedExceptions` convention (Results' `LoggedExceptions.Claim`, under its lock: the package now
  references Resrcify.SharedKernel.Results, which has no dependencies). Placed after `UseExceptionHandler()`, the request line attached every unhandled exception,
  which the mediator's behaviors or `ResultExceptionHandler` logged with its stack as well. The line now carries the
  exception only when nothing logged it before, and marks it when it does, so `ResultExceptionHandler` then logs only
  its `Debug` line. A `Logger` set through `configure` is written through the same way.

- **A request that can't be bound is a 400 problem details, not an unhandled 500** (Resrcify.SharedKernel.Web).
  `ResultExceptionHandler` answered a `BadHttpRequestException` (a minimal-API parameter that fails to bind: a
  required query or body value left out, an unparsable route segment, malformed JSON) as an unhandled exception: a 500,
  logged at `Error`. It now answers the exception's own status (400, or 413, 415 ...) with problem details holding one
  `Validation` error coded with the parameter's name (`ErrorCodes.Request.Binding`, `Request`, when it names none), e.g.
  `page`: `The required parameter "page" was not provided from query string.`, logged at `Debug`. The message names
  only the parameter, never its type or the value sent; in Development the problem's `detail` holds ASP.NET Core's
  message. **And `AddResultProblemDetails()` turns on `RouteHandlerOptions.ThrowOnBadRequest`** (after every other
  configuration of it), so Production answers these with problem details too, instead of ASP.NET Core's empty 400.
  ⚠️ A test expecting an empty 400 in Production, or a 500 in Development, for a binding failure now gets the problem
  details; `ProblemDetailsForBindingFailures = false` keeps ASP.NET Core's `ThrowOnBadRequest`. Oracle's
  `BadRequestExceptionHandler` and its `ThrowOnBadRequest = true` do the same and can be deleted.

- **The outbox processing job backs off between a message's tries, as the lanes do** (Resrcify.SharedKernel.UnitOfWork).
  It tried a failed message again at every run (about every 20 s in the services), so a handler failing through a
  downstream outage spent its 3 tries in about a minute and gave up. A failed try now writes
  `NextAttemptOnUtc` (5 s, then twice as long each time, up to 5 min: `OutboxOptions.Retries`, a `RetrySchedule`),
  and the job reads only the messages that are due, as the lanes always have; the wait is a minimum, the try comes
  with the first run or wake-up after it. The filter is a residual one on the unprocessed rows the partial index
  `IX_OutboxMessages_Unprocessed` already narrows the read to, on the `NextAttemptOnUtc` column 4.0 added: no
  migration. ⚠️ Retries are spaced out. 3 tries wait 5 s and 10 s, shorter than a 20 s run interval, so a message
  failing for a non-transient reason still gives up about two runs after its first failure; a transient failure now
  gets 12 tries, about 30 minutes (see the next entry). A test that let `OutboxWakeUp.DrainAsync()` retry a failing
  handler until the message gave up now sees `DrainAsync` return after the first failure (the message waits for a
  later try): advance the `FakeTimeProvider`, or set `Retries = new RetrySchedule(3, TimeSpan.Zero, TimeSpan.Zero)`
  in the test host, which tries again at the next run as before.

- **A transient failure gets 12 tries in the outbox, any other still 3** (Resrcify.SharedKernel.UnitOfWork), in the
  processing job and the lanes alike. With the backoff (above), the default 3 tries still gave up about a minute after a
  message's first failure, so a downstream outage longer than that made poison of its messages. A failed try now gives
  up by the limit of its failure's kind: a transient one (SharedKernel's one transient rule, see "One transient
  contract" below: a `TransientFailureException`, a cancellation or timeout, a transient database failure or a
  concurrency conflict, an HTTP request that got no answer or a 408, 429 or 5xx, anywhere among the exception's causes,
  or what a registered `ITransientFailureRule` calls transient, e.g. the Web package's for its resilience pipeline's
  timeouts and open circuits) at `TransientRetries`' attempts (12, about 30 minutes with the default delays, or
  `Retries`' attempts when that is higher), any other at `Retries`' (3), and at once when
  transient retries took the message past that (5 transient failures, then a handler's bug: it gives up on that 6th
  try). The update that marks the give-up uses
  the limit of the failure it records. The job's and the lanes' reads and the backlog monitor (the `outbox.messages.*`
  gauges and the outbox health check) take the higher limit, so a message retrying transiently past 3 is still read
  and counted as waiting, not as given up (an unmarked one past 12 still is). The give-up's `Error` counts the tries
  the message had (`Gave up at <time> after 6 tries. ...`). ⚠️ A message failing transiently is tried for about 30
  minutes before it gives up, and counts as waiting meanwhile; a test that let a handler time out (or fail with an HTTP
  5xx) until the message gave up sees 12 tries. **`TransientRetries = Retries` keeps the behaviour of 4.2** (on
  `OutboxOptions`, `OutboxLaneOptions`, or `AddProcessOutboxMessagesJob`'s `transientRetries`). Left unset,
  `TransientRetries` is `Retries` with the higher of 12 and its attempts, so a service that gave `Retries` more than 12
  attempts upgrades as it is (a transient failure gets as many tries as any other); one set with fewer attempts than
  `Retries` throws an `ArgumentOutOfRangeException` at registration (`AddOutboxProcessing`, `AddOutboxLanes`,
  `AddProcessOutboxMessagesJob`). A job stored before 4.3.0 in a persistent Quartz store, without the new job data,
  gets the same default: 12 tries for a transient failure, or its `ProcessMaxRetryCount` when that is higher.

- **The outbox logs a message's failed tries and its give-up** (Resrcify.SharedKernel.UnitOfWork), in the processing
  job and the lanes alike, under the category `Resrcify.SharedKernel.UnitOfWork.Outbox`. Nothing logged a domain event
  handler's exception (only the `Error` column and the `outbox.messages.handled` counter held it). Each failed try is
  logged once, at `Warning` when another try may pass (the transient rule, see above) and at `Error` otherwise, with the
  event type, message id, try and the tries a failure of its kind gets (`Outbox message {MessageId} ({EventType},
  {DbContext}) failed try {Attempt} of {MaxRetryCount}; the next try waits {RetryDelay}: {Error}`, 12 tries for a
  transient failure and 3 for any other by default, see above); the last one as the give-up, at `Error` (`... gave up
  after try {Attempt} ({FailureKind} failures get {MaxRetryCount} tries); it is kept as poison: {Error}`, the kind being
  `transient` or `non-transient`). The exception goes with the line, with its stack, unless something logged it
  already on its way out (`LoggedExceptions`, e.g. the mediator's unit-of-work behavior around a command the handler
  sent): then the line names its type and message only. ⚠️ An alert on `Error` logs fires for a handler's non-transient
  failure and every give-up.

- **`AddMediator` registers the scanned assemblies' FluentValidation validators** (Resrcify.SharedKernel.Mediator).
  `cfg.RegisterServicesFromAssemblies(...)` now also registers every `IValidator<T>` in those assemblies, internal ones
  included, scoped (as its `IValidator<T>` and as itself), as `AddValidatorsFromAssembly(assembly, includeInternalTypes:
  true)` does and with its `TryAddEnumerable` semantics: a validator registered already (by `AddValidatorsFromAssembly`
  before or after `AddMediator`, or by hand) is registered once, so the validation behavior never runs it twice. Only
  assemblies referencing FluentValidation are scanned for them (FluentValidation comes with the package: see "The
  packages a SharedKernel package's API is built on" under Breaking changes). `cfg.SkipValidatorScan()` turns it off. ⚠️ **Tournament's internal validators run for the first
  time**: its `AddValidatorsFromAssembly(Assembly.GetExecutingAssembly())` skipped internal types, so a request they
  reject now fails with a validation error (400) where it went through. Every service can delete its
  `services.AddValidatorsFromAssembly(...)` line (Sentinel, Tournament, Shard, Oracle and Discord in
  `Application/ApplicationServiceRegistration.cs`; Sandbox and Identity there too), and, when nothing else uses it, the
  `using FluentValidation;` with it; keeping it changes nothing.

- **`AddResultResilience` validates its options when the host starts, and owns HttpClient's timeout**
  (Resrcify.SharedKernel.Web). Its options are now the client's named `ResultResilienceOptions` (named after the
  client), validated with `ValidateOnStart`: a host whose options are unusable doesn't start, with an
  `OptionsValidationException` naming the client and every problem. It checks more than before: an `AttemptTimeout`
  longer than `TotalTimeout` or not positive, a negative circuit-breaker duration, a failure ratio outside 0-1, no
  `Retries`, and (as before) a `RetryOnly` combined with `AlsoRetry` / `NeverRetry` or holding a status outside 400-599.
  ⚠️ These threw an `InvalidOperationException` as the service registered the client; they now fail the host's start
  (or, in a container without a host, the first use of the client). A test expecting the `InvalidOperationException`
  expects an `OptionsValidationException` instead; a configuration with an attempt timeout longer than the total
  timeout (it never fired) is now refused. `RetryOnly` is copied when the options are first read (the host's start),
  not at registration. **And the client's `HttpClient.Timeout` is raised to `TotalTimeout` plus
  `HttpClientTimeoutMargin` (5 s) when it is shorter**, after every other configuration of the client, so the pipeline
  always ends a call first (as `Http.ResilienceTimeout`, never HttpClient's `TaskCanceledException`); a longer or
  infinite one the service set is kept. A service no longer keeps `TotalTimeout` "inside HttpClient's 100 s".
  `ResultResilienceOptions.DefaultRetries` is a new instance on each read (it was one shared instance, which binding
  a section would have changed in place). **Sentinel** deletes `Infrastructure/SwgohApiResilienceOptions.cs` and, in
  `AddSwgohApi`, the `AddOptions<SwgohApiResilienceOptions>()...ValidateOnStart()` block and the
  `configuration.GetSection(...).Get<...>()` read (so `AddSwgohApi` no longer takes `IConfiguration`), and calls
  `client.AddResultResilience("SwgohApiService:Resilience", resilience => { resilience.Retries = new RetrySchedule(4,
  TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), Jitter: true); resilience.AttemptTimeout = TimeSpan.FromSeconds(60);
  resilience.TotalTimeout = TimeSpan.FromSeconds(95); resilience.CircuitBreaker = false; resilience.RetryOnly =
  ResultResilienceOptions.SafeToRetryStatuses; })`; its tests (`HangingSwgohApiHostFixture`, `SwgohApiClientRetryTests`)
  write `Retries:FirstDelay` / `Retries:MaxDelay` for `RetryDelay` / `MaxRetryDelay`, under the same section. The
  services' comments about HttpClient's 100 s (Sentinel's `AddSwgohApi` remarks) can go.

- **SharedKernel's health checks are tagged by default** (Resrcify.SharedKernel.MessageBus, UnitOfWork): given no
  `tags` (`null`), `AddMessageBus()` and `AddOutbox<TDbContext>()` are tagged `HealthTags.Ready` and `AddQuartzJobs()`
  `HealthTags.Live`, where they had no tag at all (so `MapHealthEndpoints()` ran them on `/health` only). Every Titan
  service passed exactly these tags, so it can drop them (`.AddMessageBus()`, `.AddOutbox<ShardDbContext>()`,
  `.AddQuartzJobs()`, `.AddQuartzJobs(jobs => ...)`); `tags: []` registers a check with none. ⚠️ A check registered
  without tags now runs on the readiness (or liveness) probe.

- **`AddServiceTelemetry` traces Npgsql and bounds Serilog's destructuring by default** (Resrcify.SharedKernel.Observability).
  - The `"Npgsql"` activity source (`TelemetrySources.Npgsql`) is traced (a span per database command; nothing in a
    service without PostgreSQL); `NpgsqlInstrumentation = false` leaves it out. Oracle, Sentinel, Shard and
    Tournament delete `telemetry.ActivitySources.Add("Npgsql")` (`Infrastructure/InfrastructureServiceRegistration.cs`;
    `AddServiceTelemetry(name, configuration)` needs no `configure` left in Oracle, Sentinel, Shard and Tournament).
  - A destructured property (`{@...}`) is written at most 4 levels deep, 20 items a collection and 1,024 characters a
    string (`MaximumDestructuringDepth`, `MaximumDestructuredCollectionCount`, `MaximumDestructuredStringLength`; `null`
    turns one off). They are set before the `Serilog` section is read, so its `Destructure` entries and
    `ConfigureLogging` override them. ⚠️ A log line destructuring something bigger is cut (Serilog writes `…`). Discord
    deletes its `LimitDestructuring` method and `telemetry.ConfigureLogging = LimitDestructuring;`
    (`Infrastructure/InfrastructureServiceRegistration.cs`): the defaults are its values.
  - Documented: `Serilog.Enrichers.Span` / `"WithSpan"` is redundant (the `TraceId` / `SpanId` enricher was already
    there). Oracle, Sentinel, Shard, Tournament and SwgohApi drop the `Serilog.Enrichers.Span` `PackageReference`
    (`Infrastructure/*.csproj`) and its `PackageVersion`, and `"Serilog.Enrichers.Span"` from `Serilog:Using` and
    `"WithSpan"` from `Serilog:Enrich` (`Web/appsettings.Production.json`).

- **`AddServiceTelemetry`'s Serilog reads the container's sinks** (Resrcify.SharedKernel.Observability): it is set up
  with `ReadFrom.Services(provider)`, so an `ILogEventSink`, `ILogEventEnricher`, `ILogEventFilter`,
  `IDestructuringPolicy` or `LoggingLevelSwitch` registered in the container joins the logger (it is how
  `ServiceHostFixture.Logs` hears a service on Serilog). ⚠️ A service registering one of those for another purpose has
  it applied to its logs.

- **With the Postgres outbox, the outbox job and lanes claim with `PostgresOutboxLaneClaim` by default**
  (Resrcify.SharedKernel.UnitOfWork.Postgres, UnitOfWork). A context registered with `AddPostgresDbContext(..., db =>
  db.WithOutbox())` gives its outbox processing job and lanes `PostgresOutboxLaneClaim.Instance` (`FOR UPDATE SKIP
  LOCKED`) when the service set no claim (`OutboxOptions.Claim`, `OutboxLaneOptions.Claim`,
  `AddProcessOutboxMessagesJob`'s `claim`), resolved when they run, whatever the order of the registrations; a claim
  the service set wins. Without it, a service running two instances that forgot the claim published a message twice.
  ⚠️ A service on `WithOutbox()` that ran without a claim now claims (a row lock per message, in the processing
  transaction). Sentinel and Shard can delete `outbox.Claim = PostgresOutboxLaneClaim.Instance;`
  (`Infrastructure/InfrastructureServiceRegistration.cs`); ⚠️ Tournament (on `WithOutbox(...)`, no claim) now claims.

### Added

- **Web: `MapOutboxAdministration<TContext>()`'s `DELETE /messages/{id}` and `DELETE /given-up?type=`**, and
  **`IOutboxAdministration<TContext>.DeleteAsync(id)` / `DeleteAllGivenUpAsync(type)`** (Abstractions; UnitOfWork's
  `OutboxAdministration` implements them): deletes a message that gave up (204, or 404 when no message with the id
  gave up), or every one that gave up, of one type when given (`{ "deleted": n }`, `OutboxDeleted`), for good: its
  event is never handled. Only messages that gave up: a waiting one is the outbox's, a processed one the cleanup's.
  Discarding poison took hand-written SQL.

- **Abstractions: `RetrySchedule`** (`Resilience`; see Breaking changes), and the options that take one:
  `OutboxOptions.Retries` / `TransientRetries`, `OutboxLaneOptions.Retries` / `TransientRetries`,
  `AddProcessOutboxMessagesJob`'s `retries` / `transientRetries`, `ResultResilienceOptions.Retries` (with
  `DefaultRetries`), **`MessageBusBuilder.WithEventRetries(RetrySchedule)`** (new: an integration event's in-place
  tries could only be changed through Rebus' retry strategy, and their waits not at all) and
  **`RateLimitedQueueOptions.Retries`** (new, per queue).

- **Results: `ErrorCodes`**, the code of every error SharedKernel returns or answers with, nested by the package that
  emits it: `General` (`NullValue`, `EmptyInput`), `Http` (`ResilienceTimeout`, `CircuitOpen`, `Unreachable`,
  `EmptyContent`, `UnreadableContent`, `ForStatus(int)` for `Http.<status>`), `Persistence` (every `PersistenceErrors`
  code), `Request` (`Unhandled`, `Binding` = `"Request"`), `Idempotency` (`InvalidKey`, `KeyReused`, `InProgress`),
  `ScatterGather` (`Unanswered`, `UnexpectedReply`, `ResponderFailed(string requestType)`), `ErrorQueue` (`None`,
  `MessageNotFound`, `NotMoved`, `Unavailable`) and `User` (`MissingUserIdClaim`). Every SharedKernel `Error` is coded
  from it.

- **Web: `ResultResilienceErrors.Unreachable(string authority, string cause)`** (code `Http.Unreachable`) and
  `UnreachableMarker` (`unreachable`), the answer for a service that couldn't be reached (see
  above).

- **Results: `TransientFailureException`** (`Diagnostics`, carrying `ImmutableArray<Error> Errors`), **`TransientFailures`**
  (`IsTransient(Exception)`, `IsTransient(Exception, Func<Exception, bool>?)`, `TryGetErrors(Exception, out
  ImmutableArray<Error>)`, and `ThrowIfTransient()` on `Result`, `Result<T>`, `Task<Result>` and `Task<Result<T>>`,
  returning the result for chaining) and **`ErrorTypes.FromHttpStatus(int)` / `ToHttpStatus(ErrorType)`** (`Primitives`),
  the one status mapping (it was the Web package's, internal).

- **Abstractions: `Resilience/`**: `ITransientFailureRule`, `ITransientFailureClassifier`, and
  `services.AddTransientFailureRule<TRule>()`, `AddTransientFailureRule(Func<Exception, bool>)`,
  `TryAddTransientFailureClassifier()` (the default classifier: the built-in rule OR every registered rule, for the
  exception and each of its causes). The mediator's logging, unit-of-work and transaction behaviors take the classifier
  as an optional constructor parameter, and so do `CommandJob`, `AsyncCommandJob` and their one-parameter variants
  (`ITransientFailureClassifier? transientFailures = null`, last); without one, the built-in rule.

- **UnitOfWork: `PersistenceErrors.NotNullViolation`, `CheckViolation`, `ValueTooLong` and `ForeignKeyViolation`**
  (codes `Persistence.NotNullViolation`, `Persistence.CheckViolation`, `Persistence.ValueTooLong`,
  `Persistence.ForeignKeyViolation`), with their `SQLSTATE`s (`NotNullViolationSqlState`, `CheckViolationSqlState`,
  `ValueTooLongSqlState`, `ForeignKeyViolationSqlState`, `RestrictViolationSqlState`). The error returned names what
  was broken in its message when the driver reports it, e.g. `A value the database requires is missing (table
  "Members", column "Nickname").`, read from `Exception.Data` (`ConstraintName`, `TableName`, `ColumnName`, where
  Npgsql's `PostgresException` puts them), so UnitOfWork still references no provider. Never a value: PostgreSQL's
  detail, which holds the row's data, isn't read. Its message differs from the field's, so match on `Code`.

- **UnitOfWork: `OutboxOptions.Retries` / `TransientRetries` and `OutboxLaneOptions.Retries` / `TransientRetries`**
  (`RetrySchedule` / `RetrySchedule?`), and `AddProcessOutboxMessagesJob`'s `retries` / `transientRetries`: the
  attempts and the waits a message gets (see above and "One `RetrySchedule`" under Breaking changes). The lanes'
  delays were fixed at 5 s doubling to 5 min; a `FirstDelay` of `TimeSpan.Zero` tries a failed message again at once
  (the next run, or the lane's next poll). A transient failure waits by `TransientRetries`' delays.

- **Web: `ResultProblemDetailsOptions.ProblemDetailsForBindingFailures`** (`true` by default): whether
  `AddResultProblemDetails()` turns on `RouteHandlerOptions.ThrowOnBadRequest`, so a binding failure reaches
  `ResultExceptionHandler` in every environment. The code of a binding failure's error when the exception names no
  parameter is `ErrorCodes.Request.Binding` (`"Request"`).

- **Mediator: `MediatorConfiguration.SkipValidatorScan()`**, to keep `RegisterServicesFromAssemblies` from registering
  the validators (see Behaviour changes).

- **Web: `AddResultResilience(string configSectionPath, Action<ResultResilienceOptions>? configure = null)`**: the
  options set by `configure` (the service's defaults), then bound from the configuration section (`BindConfiguration`,
  the container's `IConfiguration`: the service passes none), which wins for every value it holds, `Retries`' values one
  by one; validated when the host starts. And **`ResultResilienceHttpClientBuilderExtensions.HttpClientTimeoutMargin`**
  (5 s), how far above `TotalTimeout` the client's own timeout is raised.

- **UnitOfWork.Postgres: `IHealthChecksBuilder.AddPostgres<TContext>(name = "postgres", failureStatus = Unhealthy,
  tags = [HealthTags.Ready], timeout = null)`**: the database's health check, on the connection
  `AddPostgresDbContext<TContext>` built (a pooled connection and `SELECT 1`, without `AspNetCore.HealthChecks.NpgSql`);
  without `AddPostgresDbContext<TContext>` the host fails to start, with a message saying so. It replaces
  `.AddNpgSql(sp => sp.GetPostgresConnectionString<TContext>(), name: "postgres", tags: [HealthTags.Ready])` in
  Oracle, Sentinel, Shard and Tournament (`Infrastructure/InfrastructureServiceRegistration.cs`): write
  `.AddPostgres<OracleDbContext>()` (and the others' contexts), and drop the `AspNetCore.HealthChecks.NpgSql`
  `PackageReference` (`Infrastructure/*.csproj`) and its `PackageVersion` (`Directory.Packages.props`).
- **MessageBus' README: `AddMessageBus()`'s health check replaces a RabbitMQ check.** It watches the bus' own
  connection: Sentinel and Shard delete `.AddRabbitMQ(name: "rabbitmq", tags: [HealthTags.Ready])`, the
  `services.AddSingleton<IConnection>(...)` built for it (`Infrastructure/InfrastructureServiceRegistration.cs`) and
  the `AspNetCore.HealthChecks.Rabbitmq` package (`Infrastructure/*.csproj`, `Directory.Packages.props`). SwgohApi's
  `IConnection` and `healthChecks.AddRabbitMQ(...)` (`Infrastructure/InfrastructureServiceRegistration.cs`) also watch
  the broker MassTransit uses: they go with MassTransit, for `.AddMessageBus()` on the SharedKernel bus.

- **Caching: `services.AddDistributedCaching(Action<MemoryDistributedCacheOptions>? configure = null)`**
  (`Resrcify.SharedKernel.Caching.Extensions`): the in-memory `IDistributedCache` (unless one is registered already),
  `DistributedCachingService` as the `ICachingService` and the `IClaimStore` (one singleton; one registered before is
  kept) and the system `TimeProvider` (unless one is registered), in one call. The package references
  `Microsoft.Extensions.Caching.Memory` (a dependency, see Breaking changes). It replaces the
  two lines `services.AddDistributedMemoryCache(); services.AddSingleton<ICachingService, DistributedCachingService>();`
  in Discord, Shard and Tournament (`Infrastructure/InfrastructureServiceRegistration.cs`; Sandbox and Identity there
  too, and SwgohApi's in-memory branch), and Sentinel's with `AddDistributedMemoryCache(options => options.SizeLimit =
  1024 * 1024 * 1024)`, which becomes `services.AddDistributedCaching(cache => cache.SizeLimit = 1024 * 1024 * 1024)`.
  With it the `IClaimStore` is registered too (it was found through `ICachingService` before: same instance, same
  behaviour).

- **Observability: `ServiceTelemetryOptions.NpgsqlInstrumentation`, `MaximumDestructuringDepth`,
  `MaximumDestructuredCollectionCount`, `MaximumDestructuredStringLength`, and `TelemetrySources.Npgsql`** (see
  Behaviour changes).

- **IntegrationTesting: production-like fixtures, logs and the bus harness without a fixture class per service.**
  - `PostgresContainerFixture { Image, Database, Username, Password }` and `RabbitMqContainerFixture { Image, Username,
    Password, ProductionQueueDefaults }` (init properties; the images stay production's, `postgres:18` and
    `rabbitmq:4.3.4-management`). `ProductionQueueDefaults` mounts production's `additionalConfig`
    (`ProductionQueueConfig`: quorum queues by default, an operator policy with a delivery limit of a million).
  - `RabbitMqContainerFixture.QueuesAsync(virtualHost = "/")` (each queue's name, type and whether it is exclusive, as
    `RabbitMqQueue`), `AddQuorumVirtualHostAsync(name?)` (a virtual host whose queues are quorum queues, with
    production's delivery limit; returns its `RabbitMqConnection`), `Connection` (the broker as the bus connects to
    it), `BrokerUsername` / `BrokerPassword`. SharedKernel's own `BusFixture` uses them (its `rabbitmqctl` copy is gone).
  - `ServiceHostFixture.Logs`: Microsoft's `FakeLogCollector`, holding what the host logged through the logging
    abstraction and through Serilog (`AddServiceTelemetry`'s, which reads the container's sinks). SharedKernel's three
    `CapturingLoggerProvider` test copies are replaced by `FakeLoggerProvider`.
  - `ServiceHostFixture.UsesMessageBusTestHarness` (`protected virtual bool`, off by default), `BusNetwork` and
    `Harness`: the service's buses on the message bus test harness, after its own registrations.
  - The `CreatePostgres` / `CreateRabbitMq` documentation no longer invites a fixture of the service's own for its
    image: it is production's, and `RESRCIFY_TEST_*_IMAGE` tries another.
- **MessageBus.Testing: `PublishedEventListener.StartAsync(RabbitMqConnection, listen => listen.Listen<TEvent>(wireName?))`**:
  a subscriber to a service's integration events on a real broker (a bus with an input queue of its own, bound to each
  event's topic), recording them in `Received` (a `MessageRecording`); disposing it unsubscribes.
- **Adopting the test helpers, per service** (`tests/*IntegrationTests`):
  - **Shard**: delete `Fixtures/PostgresFixture.cs` (it pinned `postgres:16`) and `Fixtures/RabbitMqFixture.cs`
    (`CreatePostgres() => new() { Database = "ShardDb", Username = "ShardUser", Password = "K33pTr4ck" }`,
    `CreateRabbitMq() => new() { Username = "messageUser", Password = "supersecretC0de" }`, or no credentials at all);
    `Harness/HarnessShardHostFixture.cs` keeps `ConfigureSettings` and `CreatePostgres` and replaces its `BusNetwork`
    and `ConfigureTestServices` with `protected override bool UsesMessageBusTestHarness => true;`; delete
    `Fixtures/TopicListener.cs` (and its `RabbitMQ.Client` use) for `PublishedEventListener`
    (`TopicListener.StartAsync(_host.Broker, nameof(ShardPayoutRotatedEventMessage))` + `WaitForEventAsync(timeout,
    bodyContains)` becomes `PublishedEventListener.StartAsync(_host.RabbitMq!.Connection, listen =>
    listen.Listen<ShardPayoutRotatedEventMessage>())` + `Received.WaitForAsync<ShardPayoutRotatedEventMessage>(e => ...,
    timeout)`, in `OutboxDispatchTests`, `OutboxWakeUpTests` and `ShardDeliveryHealthTests`).
  - **Sentinel**: delete `Fixtures/PostgresFixture.cs` (`postgres:16`) and `Fixtures/RabbitMqFixture.cs` the same
    way; `Harness/HarnessSentinelHostFixture.cs` (and the other `Harness/*HostFixture.cs` that copy its `BusNetwork` and
    `AddMessageBusTestHarness`) take `UsesMessageBusTestHarness`; the system tests' `Support/EventSubscriber.cs` can
    become a `PublishedEventListener`.
  - **Oracle**: delete `Fixtures/PostgresFixture.cs` (`postgres:16`) and `Fixtures/RabbitMqFixture.cs`, whose quorum
    `conf.d` file is `ProductionQueueDefaults = true`; `Harness/HarnessOracleHostFixture.cs` takes
    `UsesMessageBusTestHarness`.
  - **Tournament**: delete `Fixtures/QuorumRabbitMqFixture.cs` for `new() { ProductionQueueDefaults = true }`, its
    `QueuesAsync()` being the fixture's (`RabbitMqQueue(Name, Type, Exclusive)` for the tuple);
    `Harness/HarnessTournamentHostFixture.cs` takes `UsesMessageBusTestHarness` (its `StartDependenciesAsync` stand-in
    joins `BusNetwork` as before). `Fixtures/PersistenceFixture.cs` can set its credentials with init properties.
  - **Discord**: delete `Fixtures/RabbitMqFixture.cs` the same way (its `BotHostFactoryBase` isn't a
    `ServiceHostFixture`, so `BotBusHarnessFixture` keeps its own `AddMessageBusTestHarness()`).
  - Every service can drop a `CapturingLoggerProvider` of its own for `ServiceHostFixture.Logs` (or a
    `FakeLoggerProvider`), and needs no package reference for them: the package brings
    Microsoft.Extensions.Diagnostics.Testing.

### Fixed

- **A flaky message bus test** (Resrcify.SharedKernel.MessageBus, tests only):
  `IntegrationEventDispatcherTests.Handle_ShouldStillHandleTheEvent_WhenTheClaimStoreIsOutOfReach` counted
  `messagebus.events.handled event=PayoutRotated outcome=success` on the process-wide meter, which classes running in
  parallel add to (CI counted 3 for 1). The `events.*` counters carry no tag of the bus that recorded them, so
  `IntegrationEventDispatcherTests` and `EventBusTests`, which count them, now run in the new non-parallel
  `SharedMeterCollection`; the other `MetricsCapture` users count measurements tagged with their own queue or message.
  `ScatterGatherTests.Tracing_WhenItemsAreAnswered_PutsTheWholeBatchInTheOutboxMessagesTrace` counted the trace's
  handling spans as soon as the run was gathered, which the last reply's handler does before the replies' spans stop
  (seen once: 5 of 10): it now waits for them.

- **`UseServiceRequestLogging` is documented before `UseExceptionHandler()`** (its XML docs and the Observability
  README, Quick Start included), where its line logs the status the exception handler answered (a 500 whose stack the
  handler logs once, a 499 for a client abort) rather than the exception passing through. It said to put it after,
  where `UseSerilogRequestLogging()` usually went. Both placements now log the same (see above); moving it is still the
  recommended order. Oracle's reorder (`8c41d27`) is that order.

- **`ResultExceptionHandler`'s client-abort branch is gone** (Resrcify.SharedKernel.Web): it was dead.
  `UseExceptionHandler()` answers an `OperationCanceledException` or `IOException` thrown after the request was
  aborted with a 499 itself (logged at `Debug`), before it asks any `IExceptionHandler`, so the handler never saw one (a
  test over the real middleware now shows it). The Observability package's own copy, which also knew `IOException`, is
  the one left (`UseServiceRequestLogging`, which runs outside the exception handler). Nothing changes for a service.
- **Documentation drift**: AGENTS.md pointed to a missing `INTEGRATION_TESTING.md` and recommended
  `IntegrationFactoryBase` (it now points at `ServiceHostFixture` and the IntegrationTesting README), and gave EF Core
  `10.0.7` (now: the version `Directory.Packages.props` pins, `10.0.13`); the READMEs' `d:\Google Drive\...` paths are
  gone; the README's package list and layer table name MessageBus.Testing.
- **The sample is the canonical 4.3 service** (`samples/Resrcify.SharedKernel.WebApiExample`, not packed): a top-level
  `Program.cs` (no `Startup.cs`), minimal-API `IEndpoint`s on `MapPostRequest` / `MapGetRequest` (no controllers, no
  Swagger: OpenAPI in Development), `AddServiceTelemetry` / `UseServiceRequestLogging` (no `UseSerilogRequestLogging`),
  `AddPostgresDbContext` with the outbox and its wake-up, `AddOutboxProcessing`, `AddResultResilience` from
  configuration, the health checks, `AddDistributedCaching`, and the mediator registering the validators. Its
  Application project no longer references UnitOfWork or Repository, which the layer table forbids there. Its Docker
  image builds from the repository root on .NET 10, and its database is `postgres:18`.


## 4.2.0 (2026-10-14)

### Added

- **UnitOfWork: `AddIntervalCommandJob` / `AddCronCommandJob` with a command factory**, for a command without a
  parameterless constructor (a record whose parameters are all optional) or one built from services:
  `AddIntervalCommandJob<TCommand>(interval, Func<IServiceProvider, TCommand> createCommand, startAfter = null,
  timeProvider = null)`, `AddCronCommandJob<TCommand>(cronExpression, createCommand, timeZone = null, timeProvider =
  null)`, and both as `<TCommand, TResponse>`. The factory is called on every run with that run's DI scope, so it can
  read a `TimeProvider` or a scoped service; the job's key is the one the `new()` overload gives
  (`SendCommandJob-<command>`), so moving to the factory doesn't rename a job, and the command's result type is checked
  at registration as before. A second factory for the same command type, from either helper, throws
  `InvalidOperationException` at registration. The `new()` overloads are unchanged. Shard's
  `ProcessPayoutCyclesCommand(DateTime? DateTime = null)` kept a hand-written `CommandJob` for want of it:

  ```csharp
  services.AddQuartz(quartz => quartz.AddCronCommandJob<ProcessPayoutCyclesCommand>(
      "0 0/15 * * * ?",
      provider => new ProcessPayoutCyclesCommand(provider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime)));
  ```

### Fixed

- **The token-bucket limiter's rate test no longer counts wall-clock time** (Resrcify.SharedKernel.MessageBus, tests
  only; the limiter is unchanged). It counted the tokens granted in 2 s of real time at 90/s + 10 and expected 165–195;
  a busy CI runner got 131. `TokenBucketRateLimiterStrategy.CreateOptions` (internal), which `CreateLimiter` now builds
  its bucket from, is tested instead: for 1 to 1000/s the refill averages `PerSecond` in steps of at least 50 ms, the
  bucket holds at least `Burst` and one step, and 2 simulated seconds grant exactly `2 × PerSecond + Burst` (190 at
  90/s + 10).

## 4.1.0 (2026-10-14)

### Behaviour changes

- **`AddProcessOutboxMessagesJob` also runs the outbox backlog monitor**, a hosted service that measures the outbox
  every 30 s (one snapshot of four counts), as `AddOutboxProcessing` always did. A service registering the bare job now
  gets the `outbox.messages.*` gauges and a working `AddOutbox<TDbContext>()` check, for that query every 30 s.

- **`AddHealthChecks().AddOutbox<TDbContext>()` without any outbox processing fails the host's start** (an
  `OptionsValidationException` naming the missing call) instead of answering 500 on every health request.

- **`AddOutboxProcessing<TDbContext>(serializer)` throws when `serializer` isn't the registered `IOutboxSerializer`**
  (that instance, or one of its type), e.g. a custom serializer passed after `AddPostgresDbContext(... WithOutbox())`
  registered the built-in one. It used to be ignored silently, and the jobs read with the registered one. Passing
  `new SystemTextJsonOutboxSerializer()` next to `WithOutbox()`, as the services do, still works.

- **The `AddQuartzJobs` liveness check no longer watches the outbox's jobs** (processing and cleanup, every context's):
  a broker or database outage that stalls a run would restart every replica, which doesn't fix the outage, over and
  over. The outbox's stalls are reported by `AddOutbox<TDbContext>()` (Degraded on `/health/ready`) and the
  `outbox.messages.*` gauges instead, and a scheduler that stopped firing is still caught by every other job. To watch
  them as before: `AddQuartzJobs(jobs => jobs.WatchOutboxJobs = true, ...)`. Tournament's check watched them (an outbox run
  hung for about 3 minutes failed `/health/live`); Shard had excluded them by hand.

- **`AddHealthChecks().AddQuartzJobs(...)` without `AddJobHeartbeats()` fails the host's start**, naming the missing
  call, instead of answering 500 on every liveness probe.

- **`AddResultResilience` answers a timeout or an open circuit with a failed response instead of throwing.** A
  `TimeoutRejectedException` (the last attempt's or the whole call's) and a `BrokenCircuitException` escaped the
  pipeline, so a caller reading the response with `ToResultAsync` needed a try/catch (Sentinel's `UpdateGuildsJob`
  caught the timeout by hand; in Shard it became a 500). The pipeline now answers in the service's place, outermost:
  a 504 whose problem details `ToResultAsync` reads as `ResultResilienceErrors.Timeout` (`Http.ResilienceTimeout`,
  `ErrorType.Timeout`), or a 502 read as `ResultResilienceErrors.CircuitOpen` (`Http.CircuitOpen`,
  `ErrorType.ExternalFailure`, transient). The response carries the header `Resrcify-Resilience: timeout` /
  `circuit-open` and the request, and `EnsureSuccessStatusCode` throws `HttpRequestException` for it, as for a real 504
  from a proxy. The caller's own cancellation still throws `OperationCanceledException`. **On by default:** every
  service using the preset reads its responses with `ToResultAsync`, where a timeout is a failed call like a 503, not
  an exceptional one; a `catch (TimeoutRejectedException)` around such a call is now dead code and can go. Set
  `ThrowTimeoutsAndOpenCircuits = true` to keep the exceptions (as 4.0.0 and `AddStandardResilienceHandler` do).

- **The scatter-gather reply queue is named after the service** (Resrcify.SharedKernel.MessageBus):
  `<service>.scatter.replies.<machine>-<guid>`, the service being the `WithServiceName` name, else the input queue.
  A service with a name but no input queue (Tournament, Oracle) got the anonymous `scatter.replies.<machine>-<guid>`;
  one with neither still does. It stays unique per instance, durable, not exclusive and deleted by the broker 30
  minutes after its instance stops using it, so it still starts on RabbitMQ 4.3 (a quorum queue where quorum is the
  default type). ⚠️ **Behaviour change:** the reply queue of a service with an input queue moves from
  `<input queue>.replies.…` to `<input queue>.scatter.replies.…`, and a service with both a name and an input queue
  names it after the name. Nothing is lost: an instance's queue is its own, and the old ones expire 30 minutes after the
  last instance using them stops. Update a test, policy or alert matching the old names (Tournament's
  `MessageBusRegistrationTests` looks for `scatter.replies.`).

- **A custom layer gets the SharedKernel package restrictions** (Resrcify.SharedKernel.ArchitectureTesting). A layer
  added through `LayerTiers` (Discord's `Bot`) got the tier rules but no package restrictions, so it could use any
  SharedKernel package unless the service rebuilt `SharedKernelPackageRestrictions` itself. It is now restricted like
  the conventional layers at its tier (like Web at tier 4; like Infrastructure and Presentation together at tier 3;
  at a tier without one, like the nearest tier's, the inner one on a tie), or like the layer the new
  `PackageRestrictionsLike` (custom layer → layer) names, and a failure says so (`Bot → Repository: … (restricted
  like Web; …)`). A custom layer an overridden `SharedKernelPackageRestrictions` already names keeps exactly what it
  was given. ⚠️ **Behaviour change:** a custom layer using a package its tier may not use now fails
  `Layers_ShouldNotDependOnSharedKernelPackagesOutsideTheirLayers`; map it with `PackageRestrictionsLike` (or name it
  in `SharedKernelPackageRestrictions`) if that is deliberate. Discord's override adding `Bot` wherever Web is
  restricted gives the same rules and can be deleted.

- **`ServiceHostFixture` starts the host in `InitializeAsync`** (Resrcify.SharedKernel.IntegrationTesting), after
  `ConfigureSettings` and before the new `OnHostStartedAsync`, instead of on the first `CreateClient()` / `Services`
  as a plain `WebApplicationFactory` does. A host that can't start (a hosted service that throws, a missing setting
  validated on start) now fails the fixture, which tears down its containers before it throws, rather than the first
  test, with the containers left to xUnit's teardown. ⚠️ **Behaviour change:** a fixture that changed something its
  host reads (a property `ConfigureTestServices` uses, say) after `InitializeAsync` and before the first
  `CreateClient()` no longer sees the change; set it in the constructor, `StartDependenciesAsync` or
  `ConfigureSettings`.

### Added

- **Web: `ResultResilienceOptions.RetryOnly`, an allow-list of the statuses retried**, and
  `ResultResilienceOptions.SafeToRetryStatuses` (408, 429, 503), the list for a service whose callee retries its own
  failures. Set, only the listed statuses are retried (network errors and timed-out attempts still are, a 429 still
  after its `Retry-After`); any other status is returned at once and doesn't count against the circuit. It replaces
  the hand-written "every 5xx but 503" `NeverRetry` lists, which are easy to get wrong (Oracle and Tournament still
  retried 501 and 505):

  ```csharp
  client.AddResultResilience(resilience => resilience.RetryOnly = ResultResilienceOptions.SafeToRetryStatuses);
  ```

  It can't be combined with `AlsoRetry` or `NeverRetry`, and holds 400–599 only: `AddResultResilience` throws
  `InvalidOperationException` as the service registers the client, rather than pick a precedence silently.

- **Web: `ResultResilienceErrors`** (`Timeout`, `CircuitOpen`, the `Resrcify-Resilience` header) and
  `ResultResilienceOptions.ThrowTimeoutsAndOpenCircuits`: see the fix below.

- **IntegrationTesting: `ServiceHostFixture.StartDependenciesAsync(cancellationToken)`**, a hook that starts what else
  a service needs (another service's container on `Network`, say), once PostgreSQL, RabbitMQ and the network are up
  and the conventional `Settings` are in place, before `ConfigureSettings` and the host; `ConfigureSettings` reads
  what it started (a URL). **`DisposeWithFixture(x)`** (an `IAsyncDisposable`, or an `IAsyncLifetime` such as a
  container fixture) has the fixture dispose `x` on teardown: after the host has stopped, before RabbitMQ,
  PostgreSQL and the network, last first. A service that had to hand-write its fixture to start one more container
  (Sentinel's system tests, with SwgohApi's image) can use `ServiceHostFixture`.

- **Observability: `app.UseServiceRequestLogging(Action<RequestLoggingOptions>? configure = null)`**, Serilog's request
  logging the same way in every service, in place of `app.UseSerilogRequestLogging()` (which Shard and Sentinel kept
  beside `AddServiceTelemetry`). One line per request; none for the probes and the scrape (`/health`, `/health/ready`,
  `/health/live`, `/metrics`: the `UntracedPaths` the tracing already leaves out, the same list, so a path added there
  leaves both); `Error` for a 5xx or an exception, `Information` otherwise, a 4xx included (a failed result answered
  as problem details is an expected answer, not a fault of the service). Each line carries `RequestHost`, `ClientIp`
  (the connection's remote address) and `UserId` (the `ClaimTypes.NameIdentifier` claim `GetUserId()` reads, when
  present), named by the new `RequestLogProperties`, plus the request span's `TraceId` and `SpanId`, and is written
  through the host's Serilog logger rather than the static `Log.Logger`. `configure` runs on the defaults (replace
  `GetLevel`, chain onto `EnrichDiagnosticContext`). It throws `InvalidOperationException` without
  `AddServiceTelemetry` or without Serilog as the logger. The package now references `Serilog.AspNetCore` (privately,
  like its other dependencies): a service calling it references `Serilog.AspNetCore` itself, as Shard and Sentinel do.

- **IntegrationTesting: `ServiceHostFixture.OnHostStartedAsync(IServiceProvider services, CancellationToken
  cancellationToken)`**, a hook that gets the started host ready for the tests: it runs once, at the end of
  `InitializeAsync`, after the host (and its hosted services) has started and before the first test, with the host's
  root provider and a token cancelled on teardown. Sentinel's system fixture pauses its Quartz jobs there (the tests
  run them on demand) and seeds data. A failure in it tears down the host, what `DisposeWithFixture` was given and the
  containers before `InitializeAsync` throws; it doesn't run when the start failed earlier. There is no built-in
  "pause Quartz" switch: the package doesn't depend on Quartz, and which jobs to hold differs per service (the
  README shows the three lines).

- **IntegrationTesting: a `ServiceHostFixture` without PostgreSQL.** `CreatePostgres()` may return `null` (it returns
  `PostgresContainerFixture?`; an override returning the non-nullable type still compiles): no container, no
  `Database:*` or `Migrations:Run` settings, and `Postgres` throws `InvalidOperationException`. Discord's bot, which has
  no database, couldn't use the fixture.

- **`ConventionalLayerDependencyTests.PackageRestrictionsLike`** (ArchitectureTesting): custom layer → the layer whose
  SharedKernel package restrictions it takes, e.g. `{ ["Bot"] = Layers.Web }`.

- **`BaseArchitectureTest(IReadOnlyDictionary<string, Assembly>)` and
  `ConventionalLayerDependencyTests(IReadOnlyDictionary<string, Assembly>)`** (ArchitectureTesting): check the given
  layer assemblies instead of the ones found by naming convention.

- **The message bus binds the `MessageBus` section itself:** `bus.UseRabbitMqFromConfiguration()` (optionally with
  another section name) connects as the host's configuration says, validated when the host starts like
  `AddPostgresDbContext`'s `Database` section: a missing or wrong key stops the start, naming it
  (`MessageBus:Host is required.`). The keys are the ones the services' appsettings, their Kubernetes manifests
  (`MessageBus__Host`, `__Port`, `__Username`, `__Password`) and `ServiceHostFixture` already use, plus optional
  `VirtualHost` and `UseTls`. It isn't checked while nothing connects with it (the test harness, `UseInMemory()`
  afterwards), so a harness test needs no dummy `MessageBus:*` settings. The settings are the new `RabbitMqOptions`
  (`SectionName`, `ToConnection()`), registered as the default `IOptions<RabbitMqOptions>` for a service's own
  connection (Sentinel's RabbitMQ health check). `UseRabbitMq(RabbitMqConnection)` / `UseRabbitMq(Func<…>)` are
  unchanged. A service deletes its `MessageBusOptions` class, its `AddOptions<MessageBusOptions>()` registration and
  the `UseRabbitMq(provider => …)` mapping, and calls `UseRabbitMqFromConfiguration()`. The MessageBus package now
  references `Microsoft.Extensions.Options.ConfigurationExtensions`.

- **`AddOutboxProcessing<TDbContext>(configure)`, and no serializer needed.** The serializer parameter of
  `AddOutboxProcessing<TDbContext>(serializer = null, configure = null)` is optional: without one it reads with the
  registered `IOutboxSerializer` (the one `AddPostgresDbContext(... WithOutbox())` or `AddOutboxInterceptor`
  registered), or registers `SystemTextJsonOutboxSerializer` when none is. A service on the Postgres package writes
  `services.AddOutboxProcessing<ShardDbContext>(outbox => outbox.ProcessIntervalInSeconds = 20)`.

- **`AsyncCommandJob<TCommand>` / `AsyncCommandJob<TCommand, TResponse>`**: a command job whose
  `ValueTask<TCommand?> CreateCommandAsync(IJobExecutionContext context, CancellationToken cancellationToken)` can read
  first (a repository taken in its constructor) and return `null` for "nothing to send this run". A skipped run is
  logged at `Debug` and ends normally, so it beats like any run for the `AddQuartzJobs` liveness check. Failed results,
  exceptions (from `CreateCommandAsync` too) and cancellations are handled as in `CommandJob`. For jobs like
  Tournament's and Sentinel's, which may have nothing to do; `CommandJob` is unchanged.

- **`AddCronJob<TJob>(cronExpression, timeZone = null, timeProvider = null)`**, the cron counterpart of
  `AddIntervalJob`, with `AddCronCommandJob<TCommand>` / `AddCronCommandJob<TCommand, TResponse>` for the command jobs
  (`CronJobSetup`). Same job key (`IntervalJobSetup.JobKeyFor<TJob>()`, so a job moved from a hand-written setup keyed
  `nameof(TJob)` keeps its key), runs that never overlap, the trigger's start read from the registered `TimeProvider`.
  The expression is read in UTC unless a zone is given, and one that doesn't parse or never fires throws an
  `ArgumentException` at registration instead of a `FormatException` when the scheduler starts. The `AddQuartzJobs`
  liveness check judges it by the fire that is due. Replaces Shard's payouts, Sentinel's processing and Oracle's
  `*JobSetup` classes.

- **`AddQuartzJobs(Action<QuartzJobsHealthCheckOptions> configure, name, failureStatus, tags)`** chooses which jobs the
  liveness check watches: `Exclude(JobKey)`, `Exclude<TJob>()` (the key `AddIntervalJob` / `AddCronJob` give it),
  `Exclude(Func<JobKey, bool>)`, `WatchOutboxJobs` (off by default) and `MaxSilence`. Replaces Shard's
  `JobLivenessWindows.Unwatched` (`TimeSpan.MaxValue` as "don't watch"). `OutboxJobs.IsOutboxJob(JobKey)` says whether a
  key is an outbox job's.

### Fixed

- **A `ServiceHostFixture` whose start fails tears down what it had started.** A failure in a container,
  `StartDependenciesAsync`, `ConfigureSettings`, the host's own start or `OnHostStartedAsync` disposes what was started
  (as on teardown) before `InitializeAsync` throws, instead of relying on xUnit to dispose a fixture that never
  finished starting; the teardown then runs once.

- **The settings of `IntegrationFactoryBase` / `ServiceHostFixture` win over `appsettings.json` on the generic
  host.** They reached the host only through `UseSetting`, i.e. the host configuration, which
  `Host.CreateDefaultBuilder` puts before `appsettings*.json` and environment variables: a `Program` on the generic
  host (with a `Startup`) got its `appsettings.json` value instead of the fixture's. They are now also added as the
  last configuration source, so they win in both hosting models (`WebApplication` already let them win while
  registering, and still sees them then).

- **The mediator's documentation gives the standard behaviors' real order:** Logging → Idempotency → Validation →
  Transaction → UnitOfWork → Caching. `AddStandardBehaviors`, the `StandardBehavior` enum and the 4.0.0 notes left
  idempotency out, though it has run right after logging since 4.0.0 (nothing changes at run time). `StandardBehavior.Idempotency`
  is documented, and the enum says its members aren't declared in that order (`Idempotency` keeps its value): order by
  name, never by value. A behavior inserted before `Validation` runs after idempotency.

- **`AddHealthChecks().AddOutbox<TDbContext>()` no longer throws when the check runs** (a 500 on `/health` and
  `/health/ready`) for an outbox registered with `quartz.AddProcessOutboxMessagesJob<TDbContext>()` instead of
  `AddOutboxProcessing<TDbContext>()`: the job now registers the backlog monitor the check reads. Without either, the
  check is validated at start-up (the host doesn't start, and says which call is missing); a container used without a
  host gets a check reporting the same message with its failure status, never an exception. Found by Shard, Tournament
  and Sentinel adopting 4.0.0.

- **A direct `DbContext.SaveChangesAsync()` of domain events under `RetryOnFailure()` with `OnConflictDoNothing` says
  what to do.** It failed with EF Core's "The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not
  support user-initiated transactions", though the caller began none (the outbox's ON CONFLICT insert does, around the
  save). The outbox interceptor now refuses it first, before anything is written, with an `InvalidOperationException`
  that names the cause and both ways out: save through `IUnitOfWork.CompleteAsync` / `TryCompleteAsync` /
  `ExecuteInTransactionAsync`, or run the save under `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`.
  The save isn't wrapped automatically: an interceptor runs inside `SaveChangesAsync`, and the strategy has to run the
  whole save so that a retry saves it all again; doing so from inside would run every interceptor and `SavingChanges`
  handler twice and can't see `acceptAllChangesOnSuccess`. Same exception type as before; a save without domain events,
  or through the unit of work, is unaffected. Found by Sentinel in a test.

## 4.0.0 (2026-10-13)

Upgrading a service: work through **Breaking changes** below, top to bottom.

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
  `return failed.Errors;` answers with that failure from a method returning `Result<T>` (any `T`), as in 3.x; so
  does an `Error[]`. (Not to a plain `Result`, as in 3.x: write `Result.Failure(failed.Errors)`.) Passing it where an `Error[]` is expected needs `.ToArray()`. `Result.Failure` takes an
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
- **The message bus' error queue: show, delete, move back.** `IErrorQueue` (registered by `AddMessageBus`) lists the
  events whose handlers kept failing (`<input queue>.error`) with why each failed, deletes one, all or all of a type,
  and moves one, all or all of a type back to the queue it failed on, on RabbitMQ (classic and quorum queues: listing
  uses no delivery up) and in memory. A message moved back resumes at the handler that failed: those that had finished
  with it are named in a `resrcify-handled-by` header written when it was dead-lettered, and don't run again.
  `app.MapErrorQueueAdministration()` (Web) maps it to admin endpoints under `/admin/error-queue`.
- **`MessageContract.Differences<TReceived>(json)`**: checks a message as it arrived on the wire against the receiving
  side's class, for an end-to-end test that runs the real sender but doesn't have its class.
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
- **`AddStandardBehaviors()`** on the mediator configuration: Logging → Idempotency → Validation → Transaction →
  UnitOfWork → Caching, with `Without(...)`, `InsertBefore(...)` and `InsertAfter(...)` for a service's own behaviors.
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
