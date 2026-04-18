# Resrcify.SharedKernel.Messaging

`Resrcify.SharedKernel.Messaging` provides mediator-style request handling, notifications (publish/subscribe), streaming requests, and customizable pipelines.

It is designed for clean architecture use-cases where application logic is modeled as messages (`commands`, `queries`, `notifications`) with cross-cutting concerns applied via behaviors.

## Table of Contents

- [Resrcify.SharedKernel.Messaging](#resrcifysharedkernelmessaging)
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
<ProjectReference Include="..\path\to\Resrcify.SharedKernel.Messaging.csproj" />
```

### Option B: NuGet package

```xml
<PackageReference Include="Resrcify.SharedKernel.Messaging" Version="<latest>" />
```

CLI:

```powershell
dotnet add package Resrcify.SharedKernel.Messaging
```

## Quick Start

Register handlers from your application assembly and optionally add open generic behaviors.

```csharp
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Messaging.Behaviors;
using Resrcify.SharedKernel.Messaging.Extensions;
using Resrcify.SharedKernel.Messaging.Publishing;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddMediator(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(
                Assembly.GetExecutingAssembly());

            cfg.AddOpenBehavior(typeof(LoggingPipelineBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));

            cfg.UseNotificationPublishStrategy(NotificationPublishStrategy.Sequential);

            cfg.EnableDiTimePipelineComposition(enabled: false);
        });

        return services;
    }
}
```

Then consume via constructor injection:

```csharp
using Resrcify.SharedKernel.Messaging.Abstractions;

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

Messaging contracts are in `src/Resrcify.SharedKernel.Abstractions/Messaging`.

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
using Resrcify.SharedKernel.Messaging.Abstractions;
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
using Resrcify.SharedKernel.Messaging.Abstractions;
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
using Resrcify.SharedKernel.Messaging.Abstractions;

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
using Resrcify.SharedKernel.Messaging.Abstractions;

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

The package includes the following built-in behaviors in `src/Resrcify.SharedKernel.Messaging/Behaviors`:

- `LoggingPipelineBehavior<TRequest, TResponse>`
    - Logs start/end and execution time.
    - Constraint: `TResponse : Result`.
- `ValidationPipelineBehavior<TRequest, TResponse>`
    - Runs all `FluentValidation.IValidator<TRequest>` and returns validation `Result` failure when invalid.
    - Constraint: `TResponse : Result`.
- `CachingPipelineBehavior<TRequest, TResponse>`
    - Uses `ICachingService` and `ICachingQuery` (`CacheKey`, `Expiration`).
    - Constraint: `TRequest : ICachingQuery`, `TResponse : Result`.
- `TransactionPipelineBehavior<TRequest, TResponse>`
    - Wraps command execution in unit-of-work transaction.
    - Constraint: `TRequest : ITransactionalCommand`, `TResponse : Result`.
- `UnitOfWorkPipelineBehavior<TRequest, TResponse>`
    - Calls `IUnitOfWork.CompleteAsync` on successful command result.
    - Constraint: `TRequest : IBaseCommand`, `TResponse : Result`.

Add behavior(s):

```csharp
cfg.AddOpenBehavior(typeof(LoggingPipelineBehavior<,>));
cfg.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
```

> `AddOpenBehavior` accepts open generic types that implement one of:
> `IPipelineBehavior<,>`, `IRequestPipelineBehavior<,>`, `IValueTaskPipelineBehavior<,>`, `IValueTaskRequestPipelineBehavior<,>`.

## How it works internally

### Registration phase

`AddMediator(...)` scans configured assemblies and registers all discovered implementations of:

- request handlers (`IRequestHandler<,>`, `IValueTaskRequestHandler<,>`)
- stream handlers (`IStreamRequestHandler<,>`)
- notification handlers (`INotificationHandler<>`)
- behaviors and pre/post processors

### Runtime dispatch phase

`Mediator` uses compiled generic dispatch delegates + runtime caches to avoid repeated reflection work after warm-up.

- **Send path** (`Runtime/Mediator.Send.cs` + `Runtime/Mediator.SendRuntimes.cs`)
    - Resolves `ValueTask` handler first if available, otherwise task-based handler.
    - Executes pre-processors -> pipeline -> handler -> post-processors.
    - Caches runtime instances by `(RequestType, ResponseType)`.
- **Publish path** (`Runtime/Mediator.Publish.cs` + `Runtime/Mediator.PublishRuntime.cs`)
    - Resolves all handlers for a notification type.
    - Uses selected strategy (`Sequential` or `Parallel`).
    - Caches runtime per notification type.
- **Stream path** (`Runtime/Mediator.Stream.cs` + `Runtime/Mediator.StreamRuntime.cs`)
    - Resolves stream handler and stream pipeline behaviors.
    - Caches runtime instances by `(RequestType, ResponseType)`.

### Missing handler behavior

If a request/stream handler is missing, mediator throws `InvalidOperationException` and caches the message for fast-fail next calls.

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
    - Ensure validators are registered and behavior is added with `AddOpenBehavior`.
- **Caching behavior never hits cache**
    - Ensure request implements `ICachingQuery` and sets a non-empty `CacheKey`.
- **Transaction/unit-of-work behavior not applied**
    - Ensure command implements `ITransactionalCommand` / `IBaseCommand` and `IUnitOfWork` is registered.

## Sample project

See complete usage in:

- `samples/Resrcify.SharedKernel.WebApiExample`

This sample demonstrates commands, queries, result handling, and behavior composition in a realistic application flow.