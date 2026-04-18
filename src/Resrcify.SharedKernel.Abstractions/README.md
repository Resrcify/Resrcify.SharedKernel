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
    - `Messaging/`
    - `UnitOfWork/`
    - `DomainDrivenDesign/`
    - `Repository/`
    - `Caching/`
    - `Web/`
- Zero implementation logic, only interfaces/contracts.

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
    - `Resrcify.SharedKernel.Abstractions.Messaging`
    - `Resrcify.SharedKernel.Abstractions.Repository`
    - `Resrcify.SharedKernel.Abstractions.DomainDrivenDesign`
    - `Resrcify.SharedKernel.Abstractions.UnitOfWork`
- When moving files, update both namespace declarations and corresponding `using` directives in the same change.

## Usage guide

Reference this project from modules that should depend only on contracts.

```csharp
using Resrcify.SharedKernel.Abstractions.Messaging;
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
