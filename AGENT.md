# Agent Notes for Resrcify.SharedKernel

## Purpose

This repository contains the shared building blocks used across Resrcify and related services:
domain-driven design primitives, result handling, messaging behaviors, repositories, caching helpers, unit of work helpers, and web abstractions.

## Where to make changes

- `src/Resrcify.SharedKernel.DomainDrivenDesign` for domain-driven design abstractions and primitives.
- `src/Resrcify.SharedKernel.ResultFramework` for result types, errors, and helper extensions.
- `src/Resrcify.SharedKernel.Messaging` for MediatR behaviors and messaging abstractions.
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
- Keep public APIs consistent with the existing shared-kernel naming and module boundaries.

## Naming and solution conventions

- Use `Resrcify.SharedKernel.slnx` as the main solution file.
- Prefer one unit-test project per source project.
- Keep unit-test project names aligned to source modules, such as `Resrcify.SharedKernel.<Module>.UnitTests`.
- Mirror source folder structure inside test projects where practical.

## Validation

- Build or test the affected projects after changing shared abstractions, behaviors, or helpers.
- Check that new shared behaviors are covered by focused unit tests.
- Confirm the repo continues to build on .NET 10 in addition to the existing target frameworks.