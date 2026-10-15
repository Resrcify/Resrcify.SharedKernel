# Resrcify.SharedKernel

`Resrcify.SharedKernel` contains reusable building blocks used across Resrcify services:
messaging, results, domain-driven design primitives, repository support, caching, unit-of-work, web helpers, and observability.

## Table of Contents

- [Resrcify.SharedKernel](#resrcifysharedkernel)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Repository layout](#repository-layout)
  - [Build and test](#build-and-test)
  - [Development conventions](#development-conventions)
  - [Sample project](#sample-project)

Release notes, including what each major version breaks and how to upgrade: [CHANGELOG.md](CHANGELOG.md).

## What you get

- Modular shared packages under `src/`:
    - `Resrcify.SharedKernel.Abstractions`
    - `Resrcify.SharedKernel.ArchitectureTesting` (conventional NetArchTest rules for a service's test project)
    - `Resrcify.SharedKernel.Caching`
    - `Resrcify.SharedKernel.DomainDrivenDesign`
    - `Resrcify.SharedKernel.IntegrationTesting` (Testcontainers fixtures and `ServiceHostFixture<TProgram>`, a
      service hosted in-process on its own PostgreSQL and RabbitMQ)
    - `Resrcify.SharedKernel.Mediator`
    - `Resrcify.SharedKernel.MessageBus`
    - `Resrcify.SharedKernel.MessageBus.Testing` (the message bus test harness: an in-memory network, awaitable
      recordings, stand-in responders, and `PublishedEventListener` for events on a real broker)
    - `Resrcify.SharedKernel.Observability` (one-call OpenTelemetry, Prometheus, OTLP and Serilog wiring)
    - `Resrcify.SharedKernel.Repository`
    - `Resrcify.SharedKernel.Results`
    - `Resrcify.SharedKernel.UnitOfWork`
    - `Resrcify.SharedKernel.UnitOfWork.Postgres` (the PostgreSQL DbContext setup, design-time factory, `xmin` row
      versions, an outbox woken by `NOTIFY`, and the database's health check)
    - `Resrcify.SharedKernel.UnitOfWork.Web` (the outbox administration endpoints, `MapOutboxAdministration`)
    - `Resrcify.SharedKernel.Web`
- Unit-test projects under `tests/` for each source module.
- Benchmark suite in `tests/Resrcify.SharedKernel.PerformanceTests`.
- Runnable sample in `samples/Resrcify.SharedKernel.WebApiExample`.
- Moving a service off MassTransit: the migration guide in the Resrcify.SwgohApi repository
  (`docs/migrating-from-masstransit.md`), where the migration starts and ends.

### Which layer may use which package

A service's architecture tests that inherit `ConventionalLayerDependencyTests` (ArchitectureTesting) check that each
SharedKernel package is used only where it belongs. Inner layers reach the implementations through
`Resrcify.SharedKernel.Abstractions`.

| Package | Not allowed in |
|---|---|
| Abstractions, Results, DomainDrivenDesign | (allowed everywhere) |
| Mediator | Domain, Persistence, Presentation |
| MessageBus | Domain, Application, Persistence, Presentation |
| UnitOfWork | Domain, Application, Presentation |
| UnitOfWork.Postgres | Domain, Application, Presentation, Web |
| UnitOfWork.Web | Domain, Application, Presentation (as UnitOfWork, whose namespace it shares) |
| Repository | every layer but Persistence |
| Caching | Domain, Application, Persistence, Presentation |
| Web | Domain, Application, Persistence |
| Observability | Domain, Application, Persistence, Presentation |
| ArchitectureTesting, IntegrationTesting, MessageBus.Testing | every layer (tests only) |

Override `SharedKernelPackageRestrictions` (keyed by the `SharedKernelPackage` enum, layers from `Layers`) to change it.

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
# From the repository root (where Resrcify.SharedKernel.slnx is):
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