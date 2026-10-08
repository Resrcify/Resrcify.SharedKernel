# Resrcify.SharedKernel.Mediator

`Resrcify.SharedKernel.Mediator` provides mediator-style request handling, notifications (publish/subscribe), streaming requests, and customizable pipelines.

It is designed for clean architecture use-cases where application logic is modeled as messages (`commands`, `queries`, `notifications`) with cross-cutting concerns applied via behaviors.

The contracts live in `Resrcify.SharedKernel.Abstractions.Mediator`, so the Domain and Application layers depend only on the Abstractions package. For messaging between services (scatter-gather, rate-limited request queues), see `Resrcify.SharedKernel.MessageBus`.

## Table of Contents

- [Resrcify.SharedKernel.Mediator](#resrcifysharedkernelmediator)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
    - [Option A: Project reference](#option-a-project-reference)
    - [Option B: NuGet package](#option-b-nuget-package)
  - [Quick Start](#quick-start)
  - [Core Abstractions](#core-abstractions)
  - [Usage Guide](#usage-guide)
    - [Commands and Queries](#commands-and-queries)
      - [Query example](#query-example)
      - [Command example](#command-example)
      - [ValueTask handler example](#valuetask-handler-example)
    - [Notifications](#notifications)
    - [Streaming Requests](#streaming-requests)
  - [Pipeline Behaviors](#pipeline-behaviors)
    - [The standard set](#the-standard-set)
    - [The behavior check](#the-behavior-check)
    - [Logging options](#logging-options)
    - [Unit-of-work options](#unit-of-work-options)
    - [Idempotent requests](#idempotent-requests)
    - [Pre- and post-processors](#pre--and-post-processors)
  - [Tracing and metrics](#tracing-and-metrics)
  - [How it works internally](#how-it-works-internally)
    - [Registration phase](#registration-phase)
    - [Runtime dispatch phase](#runtime-dispatch-phase)
    - [Missing handler behavior](#missing-handler-behavior)
  - [Choosing runtime options](#choosing-runtime-options)
    - [Notification strategy](#notification-strategy)
    - [DI-time pipeline composition](#di-time-pipeline-composition)
  - [Common issues](#common-issues)
  - [Sample project](#sample-project)

## What you get

- `IMediator`, `ISender`, `IPublisher`, `IStreamSender` dispatch APIs.
- `IRequest<TResponse>` / `IRequestHandler<TRequest, TResponse>` for standard request-response flow.
- `IValueTaskRequestHandler<TRequest, TResponse>` for `ValueTask`-optimized handlers.
- `INotification` / `INotificationHandler<TNotification>` for one-to-many publish flow.
- `IStreamRequest<TResponse>` / `IStreamRequestHandler<TRequest, TResponse>` for async streams.
- Pipeline behavior model:
    - `IPipelineBehavior<TRequest, TResponse>`
    - `IValueTaskPipelineBehavior<TRequest, TResponse>`
    - `IRequestPipelineBehavior<TRequest, TResponse>`
    - `IValueTaskRequestPipelineBehavior<TRequest, TResponse>`
    - `IStreamPipelineBehavior<TRequest, TResponse>`
- Request pre/post processors:
    - `IRequestPreProcessor<TRequest>`
    - `IRequestPostProcessor<TRequest, TResponse>`

## Prerequisites

- .NET 10 SDK (current target is `net10.0`).
- Dependency injection via `Microsoft.Extensions.DependencyInjection`.
- Optional dependencies depending on chosen behaviors:
    - `FluentValidation` (validation behavior)
    - `Microsoft.Extensions.Logging` (logging behavior)
    - Caching abstraction implementation (`ICachingService`) for caching behavior
    - Unit-of-work implementation (`IUnitOfWork`) for transaction/unit-of-work behaviors

## Install

### Option A: Project reference

```xml
<ProjectReference Include="..\path\to\Resrcify.SharedKernel.Mediator.csproj" />
```

### Option B: NuGet package

```xml
<PackageReference Include="Resrcify.SharedKernel.Mediator" Version="<latest>" />
```

CLI:

```powershell
dotnet add package Resrcify.SharedKernel.Mediator
```

## Quick Start

Register handlers from your application assembly and add the behaviors you want. The scan registers handlers only:
a behavior or processor runs only when you add it, in the order you add it.

```csharp
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.Mediator.Publishing;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddMediator(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(
                Assembly.GetExecutingAssembly());

            // Logging -> Validation -> Transaction -> UnitOfWork -> Caching (the first is the outermost).
            cfg.AddStandardBehaviors();

            cfg.UseNotificationPublishStrategy(NotificationPublishStrategy.Sequential);

            cfg.EnableDiTimePipelineComposition(enabled: false);
        });

        return services;
    }
}
```

Then consume via constructor injection:

```csharp
using Resrcify.SharedKernel.Abstractions.Mediator;

public sealed class MyEndpoint(
    ISender sender)
{
    public Task<object?> Execute(
        object request,
        CancellationToken ct)
        => sender.Send(request, ct);
}
```

## Core Abstractions

Mediator contracts are in `src/Resrcify.SharedKernel.Abstractions/Mediator` (namespace `Resrcify.SharedKernel.Abstractions.Mediator`).

- **Requests**
    - `IRequest<TResponse>`
    - `IRequestHandler<TRequest, TResponse>`
    - `IValueTaskRequestHandler<TRequest, TResponse>`
- **Notifications**
    - `INotification`
    - `INotificationHandler<TNotification>`
- **Streams**
    - `IStreamRequest<TResponse>`
    - `IStreamRequestHandler<TRequest, TResponse>`
- **Dispatcher interfaces**
    - `ISender`, `IPublisher`, `IStreamSender`, `IMediator`
- **Domain-oriented helper abstractions**
    - `ICommand`, `ICommand<TResponse>`, `ICommandHandler<...>`
    - `IQuery<TResponse>`, `IQueryHandler<...>`
    - `ICachingQuery<TResponse>`
    - `ITransactionCommand`, `ITransactionCommand<TResponse>`

## Usage Guide

### Commands and Queries

#### Query example

```csharp
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

public sealed record GetUserByIdQuery(
    Guid Id)
    : IQuery<UserDto>;

public sealed class GetUserByIdQueryHandler
    : IQueryHandler<GetUserByIdQuery, UserDto>
{
    public Task<Result<UserDto>> Handle(
        GetUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        var dto = new UserDto(request.Id, "Ada");
        return Task.FromResult(Result.Success(dto));
    }
}
```

```csharp
Result<UserDto> result = await sender.Send(
    new GetUserByIdQuery(
        userId),
    cancellationToken);
```

#### Command example

```csharp
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

public sealed record RenameUserCommand(
    Guid Id,
    string Name)
    : ICommand;

public sealed class RenameUserCommandHandler
    : ICommandHandler<RenameUserCommand>
{
    public Task<Result> Handle(
        RenameUserCommand request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            Result.Success());
    }
}
```

```csharp
Result commandResult = await sender.Send(
    new RenameUserCommand(
        userId,
        "New Name"),
    cancellationToken);
```

#### ValueTask handler example

If you want to reduce allocations on hot paths, implement `IValueTaskRequestHandler<TRequest, TResponse>`:

```csharp
public sealed record FastPingQuery
    : IRequest<string>;

public sealed class FastPingQueryHandler
    : IValueTaskRequestHandler<FastPingQuery, string>
{
    public ValueTask<string> Handle(
        FastPingQuery request,
        CancellationToken cancellationToken)
        => ValueTask.FromResult("pong");
}
```

### Notifications

Use notifications for fan-out processing (0..n handlers).

```csharp
using Resrcify.SharedKernel.Abstractions.Mediator;

public sealed record UserCreatedNotification(Guid UserId) : INotification;

public sealed class SendWelcomeEmailHandler
    : INotificationHandler<UserCreatedNotification>
{
    public Task Handle(
        UserCreatedNotification notification,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
```

```csharp
await publisher.Publish(
    new UserCreatedNotification(
        userId),
    cancellationToken);
```

Publish behavior is configurable:

- `NotificationPublishStrategy.Sequential`: handlers run one-by-one.
- `NotificationPublishStrategy.Parallel`: handlers run with `Task.WhenAll`.

### Streaming Requests

Use stream requests for large or incremental data results.

```csharp
using System.Runtime.CompilerServices;
using Resrcify.SharedKernel.Abstractions.Mediator;

public sealed record GetNumbersStream(int Count)
    : IStreamRequest<int>;

public sealed class GetNumbersStreamHandler
    : IStreamRequestHandler<GetNumbersStream, int>
{
    public async IAsyncEnumerable<int> Handle(
        GetNumbersStream request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
            await Task.Delay(10, cancellationToken);
        }
    }
}
```

```csharp
await foreach (var item in streamSender.CreateStream(new GetNumbersStream(10), cancellationToken))
{
        // consume stream item
}
```

## Pipeline Behaviors

The package includes the following built-in behaviors in `src/Resrcify.SharedKernel.Mediator/Behaviors`:

- `LoggingPipelineBehavior<TRequest, TResponse>`
    - Logs start/end and execution time, a failure (`Warning` when another try may pass, else `Information`), an
      exception (`Error`, from an exception filter: the caller gets it unchanged), and a slow request (`Warning`; see
      [Logging options](#logging-options)).
    - Constraint: `TResponse : Result`.
- `ValidationPipelineBehavior<TRequest, TResponse>`
    - Runs the request's `FluentValidation.IValidator<TRequest>`s one at a time (they often share a scoped DbContext) and returns a validation `Result` failure when invalid.
    - Constraint: `TResponse : Result`.
- `IdempotencyPipelineBehavior<TRequest, TResponse>`
    - Answers an `IIdempotentRequest` sent again with its `IdempotencyKey` with the first one's result instead of
      handling it again (see [Idempotent requests](#idempotent-requests)).
    - Constraint: `TRequest : IIdempotentRequest`, `TResponse : Result`.
- `CachingPipelineBehavior<TRequest, TResponse>`
    - Uses `ICachingService` and `ICachingQuery` (`CacheKey`, `Expiration`). A successful result is cached for `Expiration` from the time it was cached (an absolute expiry: reads don't extend it).
    - Constraint: `TRequest : ICachingQuery`, `TResponse : Result`.
- `TransactionPipelineBehavior<TRequest, TResponse>`
    - Wraps command execution in unit-of-work transaction, at the command's `IsolationLevel` (read committed when
      `null`) and `CommandTimeout` (the DbContext's own when `null`).
    - Constraint: `TRequest : ITransactionalCommand`, `TResponse : Result`.
- `UnitOfWorkPipelineBehavior<TRequest, TResponse>`
    - Calls `IUnitOfWork.CompleteAsync` on successful command result. Runs the command through
      `IUnitOfWork.ExecuteAsync`, so when it fails (or its save is refused, or it throws) what it changed is undone in
      the change tracker and a later save in the scope doesn't save half of it.
    - With `cfg.ConfigureUnitOfWork(uow => uow.ReturnPersistenceFailures = true)`, saves with
      `IUnitOfWork.TryCompleteAsync` instead and returns its failure as the command's result (`ResultFactory.Failure`):
      a concurrency conflict or a duplicate is a `Conflict` (409), a serialization failure or a deadlock outside a
      transaction a transient `Failure`; anything else still throws. Off by default. See
      [Unit-of-work options](#unit-of-work-options).
    - Constraint: `TRequest : IBaseCommand`, `TResponse : Result`.

The logging, transaction and unit-of-work behaviors log a handler's exception (not a cancellation the caller asked
for) and let it through unchanged, so callers can catch it by type. It is logged at `Error`, with its stack, **once**:
by the first of them it passes on its way out, which is the innermost (the unit-of-work behavior for a plain command:
"Request X threw Y; nothing it changed is saved"; the transaction behavior for a transactional one, once whatever the
retries of a retrying execution strategy: "...; its transaction is rolled back"; the logging behavior for a query). The behaviors it passes after that log a `Debug` line without the stack (the logging behavior's
has the request's time). The first marks the exception (`LoggedExceptions.Claim` from Results, which sets
`Exception.Data["Resrcify.SharedKernel.Logged"]`), so a request sent from a handler doesn't log its exception again in
the sending request's behaviors, a pipeline without the logging behavior still logs it, and the Web package's
exception handler (`AddResultProblemDetails()`), which answers it with a 500 afterwards, logs only a `Debug` line.

The mediator itself is Transient (default) or Scoped; it can't be a Singleton, because it resolves handlers and
their scoped services from the provider that created it. Behaviors may still be registered as singletons.

Add behavior(s) one by one, the first added being the outermost:

```csharp
cfg.AddOpenBehavior(typeof(LoggingPipelineBehavior<,>));
cfg.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
```

> `AddOpenBehavior` accepts open generic types that implement one of:
> `IPipelineBehavior<,>`, `IRequestPipelineBehavior<,>`, `IValueTaskPipelineBehavior<,>`, `IValueTaskRequestPipelineBehavior<,>`,
> `IStreamPipelineBehavior<,>`. A closed behavior (for one request) is registered in the container:
> `services.AddTransient<IPipelineBehavior<MyRequest, Result>, MyRequestBehavior>()`.

The assembly scan doesn't register behaviors (nor pre/post-processors): a behavior in a scanned assembly runs only
once it is added like this.

### The standard set

Our services run the same chain; `AddStandardBehaviors` adds it, after any behavior added before:

```csharp
cfg.AddStandardBehaviors();   // Logging -> Validation -> Idempotency -> Transaction -> UnitOfWork -> Caching
```

Leave one out, or put the service's own behaviors in place:

```csharp
cfg.AddStandardBehaviors(standard => standard
    .InsertAfter(StandardBehavior.Validation, typeof(GuestAuthPipelineBehavior<,>), typeof(SingleFlightPipelineBehavior<,>))
    .Without(StandardBehavior.Caching));
// Logging -> Validation -> GuestAuth -> SingleFlight -> Idempotency -> Transaction -> UnitOfWork
```

`InsertBefore` works the same way. A behavior inserted next to one left out keeps its place. Inserted behaviors are
transient, and must be `IPipelineBehavior<,>`s like the standard ones: an `IRequestPipelineBehavior` always runs inside
every `IPipelineBehavior`, and a ValueTask behavior only for ValueTask handlers, so inserting one throws (add it with
`AddOpenBehavior`).

Several `AddMediator` calls (e.g. one per layer) share the logging and unit-of-work options: whichever call configures
them (`ConfigureLogging`, `ConfigureUnitOfWork`) wins over the others' defaults, and two calls configuring the same
options throw.

### The behavior check

Some requests ask for a behavior by an interface: an `ICachingQuery` must be cached, an `ITransactionalCommand` must
run in a transaction, an `IIdempotentRequest` must honour its key. Without the behavior they still run, just not cached or not in a transaction, and nothing
says so. `AddMediator` therefore throws `InvalidOperationException` when a scanned request implementing one of them
has no registered behavior that handles it: one whose request type parameter is constrained to that interface
(`CachingPipelineBehavior` and `TransactionPipelineBehavior` are; so may a service's own). It sees the behaviors added
to the configuration and those registered in the container before `AddMediator`; if yours is registered after,
call `cfg.SkipBehaviorCheck()` (with `AddMediator(assemblies)`, which takes no configuration, register it before, or
switch to `AddMediator(cfg => ...)`). The behavior must also run for the request's handler: a `Task` handler
(`IRequestHandler`) runs only Task behaviors, a ValueTask handler (`IValueTaskRequestHandler`) only ValueTask ones, so
an `ICachingQuery` with a ValueTask handler and only the standard behaviors fails the check.

### Logging options

```csharp
cfg.ConfigureLogging(logging =>
{
    logging.RequestLevel = LogLevel.Debug;                   // the "Starting/Completed request" lines (default Information)
    logging.SlowRequestThreshold = TimeSpan.FromSeconds(2);  // longer is logged at Warning (default: none)
});
```

The start/complete lines stay at `Information` by default, as before; `Debug` leaves a service's normal logs with
failures, exceptions and slow requests only. There is no slow threshold until a service sets one: what is slow
depends on the service. Durations are measured on the `TimeProvider`.

### Unit-of-work options

```csharp
cfg.ConfigureUnitOfWork(uow => uow.ReturnPersistenceFailures = true);
```

A command whose save hits a concurrency conflict (`PersistenceErrors.Concurrency`, e.g. an `xmin` row version on
PostgreSQL) or a unique constraint (`PersistenceErrors.UniqueViolation`) then fails with that error, which the Web
package answers with a 409, instead of throwing a `DbUpdateException` (a 500). The logging behavior logs it at
`Information`, as an expected answer. Inside a transactional command, a serialization failure or a deadlock still
throws: it aborted the whole transaction, so the transaction behavior's execution strategy (with retries on) runs the
command again; the transaction behavior needs no option of its own, since it rolls back on the failure the
unit-of-work behavior inside it returns.

### Idempotent requests

A request implementing `IIdempotentRequest` and sent with an `IdempotencyKey` is handled once: sent again with the key
(a client's retry, a message delivered twice), it is answered with the first result. However it arrives, an HTTP
endpoint (the `Idempotency-Key` header), a consumer (its message ID) or a job.

```csharp
public sealed record CreateShardCommand(string Name) : ICommand<ShardDto>, IIdempotentRequest
{
    public string? IdempotencyKey { get; init; }
    public string? IdempotencyScope { get; init; }   // optional: whose keys (a user's ID), so one can't replay another's
}
```

| Sent | Answer |
|---|---|
| without a key | handled as usual |
| the first time with a key | handled; the result is kept (`ICachingService`, 24 hours) while the key is held (`IClaimStore`) |
| again, after the first finished | the kept result, without handling it; `IIdempotencyContext.WasReplayed(request)` is true |
| again, while the first is handled | `IdempotencyErrors.InProgress` (a Conflict, 409) |
| again, with a different request | `IdempotencyErrors.KeyReused` (Unprocessable, 422) |
| with a blank or too long key | `IdempotencyErrors.InvalidKey` (a Validation failure, 400) |

A failure another try may pass (a transient one) isn't kept, nor is a handler that threw: a repeat runs again. The
behavior runs outside the transaction and the unit of work, so a result is kept only once committed and a repeat opens
neither. Keys are kept per request type and `IdempotencyScope`; "a different request" is told by the request's JSON
(without its key and scope), so a property whose order isn't kept (a `HashSet`) can make a true repeat look like a
different request: use a list (or sort it) in an idempotent request. It needs an `ICachingService` and an `IClaimStore` (the Caching package's
`DistributedCachingService` is both); a request sent with a key without them throws, saying so.

```csharp
cfg.ConfigureIdempotency(idempotency =>
{
    idempotency.Expiration = TimeSpan.FromHours(1);          // how long a result is kept (default 24 hours)
    idempotency.InProgressTimeout = TimeSpan.FromMinutes(5); // the longest a request holds its key (default 1 minute)
});
```

### Pre- and post-processors

Pre-processors run before the request's behaviors, post-processors after its handler. Add them like behaviors, open or
closed:

```csharp
cfg.AddRequestPreProcessor(typeof(AuditPreProcessor<>));          // every request
cfg.AddRequestPostProcessor(typeof(GetUserPostProcessor));        // the requests it implements IRequestPostProcessor<,> for
```

## Tracing and metrics

The mediator itself (whatever behaviors are registered) records every `Send`:

- **Traces**: `.AddSource(MediatorDiagnostics.ActivitySourceName)` (`Resrcify.SharedKernel.Mediator`). One span per
  request, named after its type, tagged `mediator.request.type` and `mediator.outcome` (`success`, `failure`,
  `exception`); a failure adds `mediator.error.code` and `mediator.error.type` (its first error's), an exception is
  recorded on the span. The status is `Error` for an exception and for a failure another try may fix
  (`ErrorType.IsTransient()`); a failure about the request (not found, invalid, ...) leaves it unset.
- **Metrics**: `.AddMeter(MediatorDiagnostics.MeterName)`. `mediator.request.duration` (s), tagged `request` (the
  request type's name) and `outcome` (`success`, the first error's `ErrorType`, or `exception`).

When nothing listens, a send costs one check: no span, no clock reading, no tags.

## How it works internally

### Registration phase

`AddMediator(...)` scans configured assemblies and registers all discovered implementations of:

- request handlers (`IRequestHandler<,>`, `IValueTaskRequestHandler<,>`)
- stream handlers (`IStreamRequestHandler<,>`)
- notification handlers (`INotificationHandler<>`)

Behaviors and pre/post processors are registered only as added (`AddOpenBehavior`, `AddStandardBehaviors`,
`AddRequestPreProcessor`, `AddRequestPostProcessor`, or the container), in that order. Then it runs
[the behavior check](#the-behavior-check).

### Runtime dispatch phase

`Mediator` uses compiled generic dispatch delegates, so a call does no reflection after warm-up. The mediator is
transient: it keeps only the last runtime it built for each kind of call (send, publish, stream), reused when its next
call has the same types (e.g. a job sending one command per item).

- **Send path** (`Runtime/Mediator.Send.cs` + `Runtime/Mediator.SendRuntimes.cs`)
    - Resolves `ValueTask` handler first if available, otherwise task-based handler.
    - Executes pre-processors -> pipeline -> handler -> post-processors.
    - Keeps the last send runtime (one slot).
- **Publish path** (`Runtime/Mediator.Publish.cs` + `Runtime/Mediator.PublishRuntime.cs`)
    - Resolves all handlers for a notification type.
    - Uses selected strategy (`Sequential` or `Parallel`).
    - Keeps the last publish runtime (one slot).
- **Stream path** (`Runtime/Mediator.Stream.cs` + `Runtime/Mediator.StreamRuntime.cs`)
    - Resolves stream handler and stream pipeline behaviors.
    - Keeps the last stream runtime (one slot).

### Missing handler behavior

If a request/stream handler is missing, the mediator throws `InvalidOperationException` ("No request handler
registered for ..."), on every call: nothing about the failure is cached.

## Choosing runtime options

### Notification strategy

```csharp
cfg.UseNotificationPublishStrategy(
    NotificationPublishStrategy.Parallel);
```

Use `Sequential` when handler order/serialization matters; use `Parallel` for throughput when handlers are independent and thread-safe.

### DI-time pipeline composition

```csharp
cfg.EnableDiTimePipelineComposition(true);
```

When enabled, `DiComposedSendRuntime<,>` / `DiComposedStreamRuntime<,>` are built through DI and reused by mediator runtime lookup. This can reduce per-call composition overhead in some scenarios.

## Common issues

- **"No request handler registered for '...'"**
    - Ensure `RegisterServicesFromAssemblies(...)` includes the assembly containing your handler.
- **Validation behavior does nothing**
    - Ensure validators are registered and behavior is added with `AddOpenBehavior` (or `AddStandardBehaviors`).
- **A behavior or processor in my assembly doesn't run**
    - The scan registers handlers only: add it with `AddOpenBehavior` / `AddRequestPreProcessor` /
      `AddRequestPostProcessor`, or register a closed one in the container.
- **"... request type(s) implement ICachingQuery ..., but no registered pipeline behavior handles ICachingQuery"**
    - Add the caching behavior (`AddStandardBehaviors()` includes it), or, if it is registered after `AddMediator`,
      call `SkipBehaviorCheck()`.
- **Caching behavior never hits cache**
    - Ensure request implements `ICachingQuery` and sets a non-empty `CacheKey`.
- **Transaction/unit-of-work behavior not applied**
    - Ensure command implements `ITransactionalCommand` / `IBaseCommand` and `IUnitOfWork` is registered.

## Sample project

See complete usage in:

- `samples/Resrcify.SharedKernel.WebApiExample`

This sample demonstrates commands, queries, result handling, and behavior composition in a realistic application flow.