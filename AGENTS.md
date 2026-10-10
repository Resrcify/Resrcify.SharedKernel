# Agent Notes for Resrcify.SharedKernel

## Purpose

This repository contains the shared building blocks used across Resrcify and related services:
domain-driven design primitives, result handling, messaging behaviors, repositories, caching helpers, unit of work helpers, and web abstractions.

## Where to make changes

- `src/Resrcify.SharedKernel.DomainDrivenDesign` for domain-driven design abstractions and primitives.
- `src/Resrcify.SharedKernel.Results` for result types, errors, and helper extensions.
- `src/Resrcify.SharedKernel.Mediator` for the in-process mediator: dispatch, pipeline behaviors, notification publishing.
- `src/Resrcify.SharedKernel.MessageBus` for the message bus (Rebus on RabbitMQ, or in memory): scatter-gather through the outbox, rate-limited request queues (with a mediator-forwarding default responder), publish/subscribe integration events (duplicate skipping, partitions, clash detection), health check, metrics, message contracts. It depends only on `Abstractions` (it reaches the outbox through `IOutboxLaneEvent` / `IOutboxMessageContext`), never on `UnitOfWork`.
- `src/Resrcify.SharedKernel.ArchitectureTesting` for the conventional NetArchTest rules services inherit, including which layer may use which SharedKernel package (`ConventionalLayerDependencyTests.SharedKernelPackageRestrictions`).
- `src/Resrcify.SharedKernel.Repository` for repository abstractions and query helpers.
- `src/Resrcify.SharedKernel.Caching` for cache abstractions and cache helpers.
- `src/Resrcify.SharedKernel.UnitOfWork` for unit-of-work, background job, and outbox helpers (outbox lanes, the hourly cleanup of processed messages after 7 days, stable IDs for retried messages, draining and waking the outbox, `TryCompleteAsync`, migrations on start-up, the Quartz helpers: interval and command jobs, job heartbeats and the liveness check).
- `src/Resrcify.SharedKernel.UnitOfWork.Postgres` for the PostgreSQL side: `AddPostgresDbContext` (the `Database` section, interceptors from the container, outbox, retries), `PostgresDesignTimeFactory`, the `xmin` row version and the outbox wake-up (`NOTIFY`/`LISTEN`). Npgsql-specific code belongs here, not in `UnitOfWork`.
- `src/Resrcify.SharedKernel.Web` for web extensions and request/response helpers: problem details (also for unhandled exceptions), reading responses back as results, the health endpoints, the `ErrorType`-keyed HTTP resilience preset and the shared JWT bearer setup.
- `src/Resrcify.SharedKernel.Observability` for the one-call telemetry wiring (`AddServiceTelemetry`: OpenTelemetry, Prometheus, OTLP, Serilog). It subscribes to the other packages' sources and meters by name (`Resrcify.SharedKernel.*`), never by reference: name a new `ActivitySource`/`Meter` under that prefix.
- `samples/Resrcify.SharedKernel.WebApiExample` for example host wiring and usage.

## Runtime rules

- Keep shared abstractions small and reusable across services.
- Prefer behavior implemented once in the shared kernel over duplicate logic in downstream apps.
- Validation, result handling, and pipeline behaviors should remain framework-agnostic where possible.

## Editing guidance

- Prefer small, focused changes in the owning module.
- When adding a new feature or behavior, add corresponding unit tests in the matching `*.UnitTests` project.
- Prefer tall code: keep logic vertically readable, with small methods and explicit flow over dense one-liners.
- Keep method declarations tall: put each parameter on its own line for multi-parameter methods.
- Keep inheritance and interface implementation tall: place `:` and each inherited type on separate lines.
- Keep `SuppressMessage` attributes tall and always include an explicit `Justification` reason.
- Keep public APIs consistent with the existing shared-kernel naming and module boundaries.
- Place interfaces by reach: an interface used only within one project goes in that project's own `Abstractions/` folder (namespace `Resrcify.SharedKernel.<Module>.Abstractions`); an interface shared across projects goes in the `Resrcify.SharedKernel.Abstractions` project, in the sub-folder matching its concern.
- Keep namespace declarations aligned to folder structure from `src/` and `tests/` roots.
- Ensure the namespace root reflects the owning module path (for example, `Abstractions/Mediator` maps to `Resrcify.SharedKernel.Abstractions.Mediator`).
- When moving files between folders, update namespace declarations and related `using` directives in the same change.
- Preserve existing project style and avoid unrelated refactors while applying namespace corrections.
- Read the clock through `TimeProvider` (from DI, or an optional constructor parameter defaulting to `TimeProvider.System`), never `DateTime.UtcNow` / `DateTimeOffset.UtcNow`, and wait with its overloads (`Task.Delay(delay, timeProvider, ct)`, `new PeriodicTimer(interval, timeProvider)`), so services can test time with `FakeTimeProvider`.
- Record every change a consumer can notice in `CHANGELOG.md`, under the unreleased version: a breaking one under "Breaking changes" with what the consumer must do, the rest under Added / Fixed / Removed.
- Every package lists its public API in `PublicAPI.Shipped.txt` (released) and `PublicAPI.Unshipped.txt` (since), next to its project; a public member added, removed or changed without its line fails the build (RS0016 / RS0017). Add the line with the change. On a release, move each package's Unshipped lines into its Shipped file. To regenerate a package's lines, build with `-p:ErrorLog=<file>.sarif%2Cversion=2.1` and take each RS0016's `APIName` (`dotnet format` can't apply that fix).

## Naming and solution conventions

- Use `Resrcify.SharedKernel.slnx` as the main solution file.
- Version every project together: a release applies the same version tag/number to all projects in the solution — never version packages independently.
- Prefer one unit-test project per source project.
- Keep unit-test project names aligned to source modules, such as `Resrcify.SharedKernel.<Module>.UnitTests`.
- Mirror source folder structure inside test projects where practical.

## Validation

Climb in order — stop at the rung that proves or disproves the hypothesis. Don't skip rungs and don't iterate on production.

### 1. Read the code

Trace the suspect path by hand. Most bugs in well-typed C# are visible if read carefully. If the symptom doesn't match anything in the visible code, escalate.

### 2. Unit test — `*.UnitTests` projects

Write a focused test that exercises the actual call shape — for dispatch / overload-resolution issues this means passing the argument typed exactly like the production call site (often a base interface like `IDomainEvent`, not a concrete type). No DB, no MQ, no docker. See `tests/Resrcify.SharedKernel.Mediator.UnitTests/Runtime/MediatorRuntimeTests.cs#Publish_DispatchesByRuntimeType_WhenCallerHoldsBaseInterfaceVariable` for a template.

```sh
dotnet test Resrcify.SharedKernel.slnx --no-build
```

### 3. Integration test — `Resrcify.SharedKernel.IntegrationTesting`

Use the shipped fixtures (`PostgresContainerFixture`, `RabbitMqContainerFixture`, `IntegrationFactoryBase<TProgram>`) when the symptom requires real infra (transaction interleaving, exchange routing, migration application against a fresh DB, end-to-end outbox dispatch). Each fixture spins up its own real Postgres / RabbitMQ in docker and tears down on dispose — no manual orchestration. Reference cover for the outbox dispatch path lives at `tests/Resrcify.SharedKernel.UnitOfWork.IntegrationTests/OutboxDispatchTests.cs`. Pattern + per-service rollout in `INTEGRATION_TESTING.md`.

```sh
dotnet test tests/Resrcify.SharedKernel.UnitOfWork.IntegrationTests
```

Requires Docker on the runner. CI (`build-and-test.yml`) has it on `ubuntu-latest`.

The fixtures default to the images production runs (`postgres:18`; `rabbitmq:4.3.4-management`, the RabbitMQ Cluster Operator v2.23.0's default: move it with the operator, as each 4.x refuses more deprecated broker features); don't pin an older one with `WithImage`. To run a suite against another version without editing code, set `RESRCIFY_TEST_POSTGRES_IMAGE` / `RESRCIFY_TEST_RABBITMQ_IMAGE` — they win over a fixture's own `WithImage`.

### 4. Architecture test — `Resrcify.SharedKernel.ArchitectureTesting`

Inherit `ConventionalLayerDependencyTests`, `ConventionalDomainTests`, `ConventionalApplicationTests`, `ConventionalPresentationTests` from a service's `*.ArchitectureTests` project. Layer assemblies are auto-discovered via naming convention. Validate that a change doesn't quietly violate the dependency direction or the naming conventions. See the migrated sample at `samples/Resrcify.SharedKernel.WebApiExample/tests/Resrcify.SharedKernel.WebApiExample.ArchitectureTests/`.

### 5. Manual docker-compose + `dotnet run`

Last resort before prod. Reserved for symptoms that only emerge under full process startup and only after rungs 1–4 have been ruled out. Treat this as a sign rungs 2–4 are missing coverage and add a Testcontainers test once the bug is understood.

### 6. Production

Never as a debug step. If you don't have a passing test at rungs 2–4, you don't have a fix.

### Solution-wide checks before pushing a release tag

- `dotnet build Resrcify.SharedKernel.slnx --no-restore` — clean, 0 warnings, 0 errors.
- `dotnet test Resrcify.SharedKernel.slnx --no-build` — all test projects pass, no `Test Run Aborted`.
- New library packages need `<IsTestProject>false</IsTestProject>` in the csproj — `dotnet test` otherwise treats any project that references `xunit` as a test project and aborts when it finds no tests, blocking the CI publish step.
- Confirm the repo still builds on .NET 10.
## Operations & runbook

- Production/deployment incident findings for this project live in [`docs/OPERATIONS.md`](docs/OPERATIONS.md) — read it before debugging a prod issue, and add a section (Symptom -> Root cause -> Fix -> Verification) whenever you resolve one.
- Project-local Claude config/memory lives in [`.claude/`](.claude/); keep durable knowledge in this repo's `docs/`, not in the shared workspace memory bucket.

## Working in this environment (agent quirks)

Things that have bitten past agents and don't surface in an error message until you hit them.

### Pull requests
- **`gh` CLI is installed at `C:\Program Files\GitHub CLI\gh.exe`** but is frequently *not on `PATH`* in shells Claude Code spawns (the session env forks before installer PATH updates apply). Fallbacks: call the full path via PowerShell (`& "C:\Program Files\GitHub CLI\gh.exe" pr create ...`) or extend PATH per-call (`$env:PATH = "$env:PATH;C:\Program Files\GitHub CLI"; gh ...`). Check first: `Get-Command gh -ErrorAction SilentlyContinue`.
- **`gh auth login` is interactive — agents can't run it.** Run `gh auth status` first; if not logged in, push the branch and surface the compare URL `https://github.com/Resrcify/Resrcify.SharedKernel/compare/master...<branch>?expand=1` with the title + markdown body inline so the user can paste both.
- The user pushes directly to `master` (single-maintainer). The harness blocks default-branch pushes by default; expect a permission prompt.

### Build is the lint step
- `TreatWarningsAsErrors` + `AnalysisMode=all` + SonarAnalyzer.CSharp are on globally (`Directory.Build.props`). **Resolve warnings, don't suppress.** CA1873 (potentially-expensive log args) is an error in .NET 10 — wrap chained `.Value`/`?.X` log calls in `if (_logger.IsEnabled(LogLevel.X))` guards.
- Prefer **tall code**: small methods, explicit vertical flow, one parameter per line for multi-param methods — over dense one-liners.
- After touching the mediator pipeline, repository contracts, or EF configuration, build the full solution and run the domain tests.

### EF Core migrations (if this repo has a Persistence project)
- Needs `Microsoft.EntityFrameworkCore.Tools` + an explicit `Microsoft.EntityFrameworkCore.Design 10.0.7` `PackageReference` in the Persistence csproj (**not** transitive — a transitive `Design 8.0.0` breaks the tool with `MissingMethodException: AbstractionsStrings.ArgumentIsEmpty`) + the global `dotnet-ef` at `10.0.7`.
- Run from the Persistence project (it owns its `IDesignTimeDbContextFactory`); point both `--project` and `--startup-project` at it.
- The tool re-runs `dotnet build` internally and ignores outer `-p:NoWarn`; pass MSBuild props after `--`: `dotnet ef migrations add <Name> --project ... --startup-project ... -- --property:NoWarn=<rule>`.
- Scaffolded drop-table migrations **do not preserve data** — hand-edit to backfill before dropping. Don't touch `.Designer.cs` or the model snapshot.
- Migration files inherit `TreatWarningsAsErrors`: use `Array.Empty<string>()` (CA1825) and keep `using System;`.
