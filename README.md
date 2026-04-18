# Resrcify.SharedKernel

`Resrcify.SharedKernel` contains reusable building blocks used across Resrcify services:
messaging, results, domain-driven design primitives, repository support, caching, unit-of-work, and web helpers.

## Table of Contents

- [Resrcify.SharedKernel](#resrcifysharedkernel)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Repository layout](#repository-layout)
  - [Build and test](#build-and-test)
  - [Development conventions](#development-conventions)
  - [Sample project](#sample-project)

## What you get

- Modular shared packages under `src/`:
    - `Resrcify.SharedKernel.Abstractions`
    - `Resrcify.SharedKernel.Caching`
    - `Resrcify.SharedKernel.DomainDrivenDesign`
    - `Resrcify.SharedKernel.Messaging`
    - `Resrcify.SharedKernel.Repository`
    - `Resrcify.SharedKernel.Results`
    - `Resrcify.SharedKernel.UnitOfWork`
    - `Resrcify.SharedKernel.Web`
- Unit-test projects under `tests/` for each source module.
- Benchmark suite in `tests/Resrcify.SharedKernel.PerformanceTests`.
- Runnable sample in `samples/Resrcify.SharedKernel.WebApiExample`.

## Prerequisites

- .NET 10 SDK.
- Optional Docker tooling for the sample application.

## Repository layout

- Main solution file: `Resrcify.SharedKernel.slnx`.
- Source modules: `src/`.
- Tests and benchmarks: `tests/`.
- Sample host: `samples/Resrcify.SharedKernel.WebApiExample`.

## Build and test

```powershell
Set-Location "d:\Google Drive\Projects\Titan404\Resrcify.SharedKernel"
dotnet restore .\Resrcify.SharedKernel.slnx
dotnet build .\Resrcify.SharedKernel.slnx
dotnet test .\Resrcify.SharedKernel.slnx
```

## Development conventions

- Keep changes focused in the owning module.
- Add or update matching unit tests for new behaviors.
- Keep code vertically readable (“tall” style).
- Keep namespaces aligned to folder structure.

## Sample project

See `samples/Resrcify.SharedKernel.WebApiExample` for end-to-end usage across modules.