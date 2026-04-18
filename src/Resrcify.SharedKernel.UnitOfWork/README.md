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

### Outbox processing job

```csharp
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

services.AddQuartz();
services.AddQuartzHostedService(options =>
    options.WaitForJobsToComplete = true);

services.ConfigureOptions<ProcessOutboxMessagesJobSetup<AppDbContext>>();
```

## Related modules

- `Resrcify.SharedKernel.Messaging` includes `UnitOfWorkPipelineBehavior<,>` and `TransactionPipelineBehavior<,>` built on this module.
- `Resrcify.SharedKernel.DomainDrivenDesign` provides domain event abstractions consumed by outbox processing.