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
    - [Outbox table mapping](#outbox-table-mapping)
    - [Outbox serializer](#outbox-serializer)
    - [Wiring it up](#wiring-it-up)
    - [Outbox processing job](#outbox-processing-job)
  - [Related modules](#related-modules)

## What you get

- `IUnitOfWork` contract in `Resrcify.SharedKernel.Abstractions.UnitOfWork`.
- EF Core-backed `UnitOfWork<TDbContext>` implementation.
- Outbox/background-job helpers in `BackgroundJobs/`.
- Interceptors for domain events and auditable/deletable entities.

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
    timeout: TimeSpan.FromSeconds(30),
    cancellationToken: cancellationToken);

try
{
    await unitOfWork.CompleteAsync(cancellationToken);
    await unitOfWork.CommitTransaction(cancellationToken);
}
catch
{
    await unitOfWork.RollbackTransactionAsync(cancellationToken);
    throw;
}
```

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
and deserializes them on the read side (job). Two are built in —
`SystemTextJsonOutboxSerializer` and `NewtonsoftJsonOutboxSerializer` — and both
embed the concrete type in the payload. Create one instance and share it so the two
sides cannot drift apart.

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

### Outbox processing job

The job reads a batch with a no-tracking projection (served by the covering index).
Each message is then handled in its **own DI scope and transaction** (via
`IUnitOfWork.ExecuteInTransactionAsync`): publishing the event and marking the
message processed commit together, so the message is only marked done if everything
its handlers persisted commits as well. Because every message gets a fresh scope, the
job never shares (or clears) a `DbContext` change tracker with other work. A handler
that throws does not abort the batch — the failure is written to the `Error` column
and `RetryCount` is incremented; once `RetryCount` reaches the configured maximum the
message is treated as poison and the polling query skips it (it stays in the table,
unprocessed, for inspection).

## Related modules

- `Resrcify.SharedKernel.Messaging` includes `UnitOfWorkPipelineBehavior<,>` and `TransactionPipelineBehavior<,>` built on this module.
- `Resrcify.SharedKernel.DomainDrivenDesign` provides domain event abstractions consumed by outbox processing.