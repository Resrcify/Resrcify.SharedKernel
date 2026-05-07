# Agent Notes for Resrcify.SharedKernel

## Purpose

This repository contains the shared building blocks used across Resrcify and related services:
domain-driven design primitives, result handling, messaging behaviors, repositories, caching helpers, unit of work helpers, and web abstractions.

## Where to make changes

- `src/Resrcify.SharedKernel.DomainDrivenDesign` for domain-driven design abstractions and primitives.
- `src/Resrcify.SharedKernel.Results` for result types, errors, and helper extensions.
- `src/Resrcify.SharedKernel.Messaging` for mediator behaviors and messaging abstractions.
- `src/Resrcify.SharedKernel.Repository` for repository abstractions and query helpers.
- `src/Resrcify.SharedKernel.Caching` for cache abstractions and cache helpers.
- `src/Resrcify.SharedKernel.UnitOfWork` for unit-of-work, background job, and outbox helpers.
- `src/Resrcify.SharedKernel.Web` for web extensions and request/response helpers.
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
- Keep namespace declarations aligned to folder structure from `src/` and `tests/` roots.
- Ensure the namespace root reflects the owning module path (for example, `Abstractions/Messaging` maps to `Resrcify.SharedKernel.Abstractions.Messaging`).
- When moving files between folders, update namespace declarations and related `using` directives in the same change.
- Preserve existing project style and avoid unrelated refactors while applying namespace corrections.

## Naming and solution conventions

- Use `Resrcify.SharedKernel.slnx` as the main solution file.
- Prefer one unit-test project per source project.
- Keep unit-test project names aligned to source modules, such as `Resrcify.SharedKernel.<Module>.UnitTests`.
- Mirror source folder structure inside test projects where practical.

## Validation

Climb in order — stop at the rung that proves or disproves the hypothesis. Don't skip rungs and don't iterate on production.

### 1. Read the code

Trace the suspect path by hand. Most bugs in well-typed C# are visible if read carefully. If the symptom doesn't match anything in the visible code, escalate.

### 2. Unit test — `*.UnitTests` projects

Write a focused test that exercises the actual call shape — for dispatch / overload-resolution issues this means passing the argument typed exactly like the production call site (often a base interface like `IDomainEvent`, not a concrete type). No DB, no MQ, no docker. See `tests/Resrcify.SharedKernel.Messaging.UnitTests/Runtime/MediatorRuntimeTests.cs#Publish_DispatchesByRuntimeType_WhenCallerHoldsBaseInterfaceVariable` for a template.

```sh
dotnet test Resrcify.SharedKernel.slnx --no-build
```

### 3. Integration test — `Resrcify.SharedKernel.IntegrationTesting`

Use the shipped fixtures (`PostgresContainerFixture`, `RabbitMqContainerFixture`, `IntegrationFactoryBase<TProgram>`) when the symptom requires real infra (transaction interleaving, exchange routing, migration application against a fresh DB, end-to-end outbox dispatch). Each fixture spins up its own real Postgres / RabbitMQ in docker and tears down on dispose — no manual orchestration. Reference cover for the outbox dispatch path lives at `tests/Resrcify.SharedKernel.UnitOfWork.IntegrationTests/OutboxDispatchTests.cs`. Pattern + per-service rollout in `INTEGRATION_TESTING.md`.

```sh
dotnet test tests/Resrcify.SharedKernel.UnitOfWork.IntegrationTests
```

Requires Docker on the runner. CI (`build-and-test.yml`) has it on `ubuntu-latest`.

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