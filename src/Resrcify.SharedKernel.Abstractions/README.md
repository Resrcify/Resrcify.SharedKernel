# Resrcify.SharedKernel.Abstractions

`Resrcify.SharedKernel.Abstractions` contains shared contracts used by other SharedKernel modules.

It exists to keep dependency direction explicit and prevent circular references between implementation projects.

## Table of Contents

- [Resrcify.SharedKernel.Abstractions](#resrcifysharedkernelabstractions)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
        - [Option A: Project reference](#option-a-project-reference)
        - [Option B: NuGet package](#option-b-nuget-package)
  - [Folder and namespace conventions](#folder-and-namespace-conventions)
  - [Usage guide](#usage-guide)
  - [Common issues](#common-issues)

## What you get

- Contracts grouped by module under `src/Resrcify.SharedKernel.Abstractions`:
    - `Mediator/`: the requests, handlers and dispatchers, and the markers the mediator's behaviors act on:
      `ICachingQuery`, `ITransactionalCommand`, `IIdempotentRequest` (answered with the first result when sent again
      with its key) and `INonConcurrentRequest` (handled one at a time per request type and `ConcurrencyKey`: another
      waits, then runs itself; per process, not across replicas; see the Mediator package's "Non-concurrent requests")
    - `MessageBus/`
    - `UnitOfWork/`
    - `DomainDrivenDesign/`
    - `Repository/`
    - `Caching/`
    - `Resilience/`: `ITransientFailureRule` and `ITransientFailureClassifier`, the hook that adds a package's or a
      service's own rules to Results' transient rule (`services.AddTransientFailureRule<TRule>()`,
      `AddTransientFailureRule(Func<Exception, bool>)`, `TryAddTransientFailureClassifier()`, which registers the
      default classifier: the built-in rule OR every registered rule, for the exception and each of its causes), and
      `RetrySchedule(MaxAttempts, FirstDelay, MaxDelay, Jitter)`, SharedKernel's one backoff: attempts in all, the
      first delay doubling up to the longest (`DelayAfter(failedAttempt)`), which the outbox, the message bus and the
      HTTP resilience pipeline take as their retry options.
    - `Metrics/`: `DurationBuckets.Seconds`, the bucket boundaries (5 ms to 1 h) every SharedKernel duration histogram
      in seconds advises, for a service's own too:
      `advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets.Seconds }`. Without advice,
      OpenTelemetry buckets a histogram for milliseconds, and durations in seconds all land in the first bucket.
- Interfaces/contracts only, but for the resilience registration and its default classifier, and the duration buckets. (Some interfaces carry a
  default: `ISingleValueObject`'s static `FromPersisted` is `Create(value).Value` unless the value object implements
  it; `IIdempotentRequest.IdempotencyScope` and `INonConcurrentRequest.ConcurrencyKey` are `null` unless the request
  sets them.)

## Prerequisites

- .NET 10 SDK.

## Install

### Option A: Project reference

```xml
<ProjectReference Include="..\path\to\Resrcify.SharedKernel.Abstractions.csproj" />
```

### Option B: NuGet package

```xml
<PackageReference Include="Resrcify.SharedKernel.Abstractions" Version="<latest>" />
```

CLI:

```powershell
dotnet add package Resrcify.SharedKernel.Abstractions
```

## Folder and namespace conventions

- Keep namespace declarations aligned to folder structure from `src/` and `tests/` roots.
- Use module-rooted namespaces such as:
    - `Resrcify.SharedKernel.Abstractions.Mediator`
    - `Resrcify.SharedKernel.Abstractions.Repository`
    - `Resrcify.SharedKernel.Abstractions.DomainDrivenDesign`
    - `Resrcify.SharedKernel.Abstractions.UnitOfWork`
- When moving files, update both namespace declarations and corresponding `using` directives in the same change.

## Usage guide

Reference this project from modules that should depend only on contracts.

```csharp
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;

public sealed class ExampleService(
    ISender sender,
    IUnitOfWork unitOfWork)
{
    public async Task ExecuteAsync(
        IRequest<Result> request,
        CancellationToken cancellationToken)
    {
        await sender.Send(request, cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
    }
}
```

## Common issues

- If a module starts referencing implementation projects just for interfaces, move those contracts into this project.
- If namespace drift appears after file moves, run a namespace audit and fix declarations immediately.
