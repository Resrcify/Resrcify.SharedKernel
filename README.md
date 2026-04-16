# Resrcify.SharedKernel

Shared building blocks used across Resrcify services and samples.

## What’s inside

- `Resrcify.SharedKernel.Caching` for cache abstractions and cache helpers.
- `Resrcify.SharedKernel.DomainDrivenDesign` for entities, value objects, and domain primitives.
- `Resrcify.SharedKernel.Messaging` for MediatR pipeline behaviors and messaging abstractions.
- `Resrcify.SharedKernel.Repository` for repository abstractions and query helpers.
- `Resrcify.SharedKernel.ResultFramework` for result types and error handling.
- `Resrcify.SharedKernel.UnitOfWork` for unit-of-work helpers, outbox support, and background jobs.
- `Resrcify.SharedKernel.Web` for web extensions and request/response helpers.
- `samples/Resrcify.SharedKernel.WebApiExample` for a runnable example application.

## Solution layout

- Use `Resrcify.SharedKernel.slnx` as the primary solution file.
- Keep one unit-test project per source project.
- Mirror source folders in tests where it improves discoverability.

## Runtime support

- Targets `net8.0`, `net9.0`, and `net10.0`.
- Shared packages are published separately by module.
- CI builds should restore and test against the `.slnx` solution file.

## Contributing

1. Create a branch for your change.
2. Keep the change focused in the owning module.
3. Add or update unit tests for each new feature or behavior.
4. Prefer vertically readable, "tall" code with small methods and explicit flow.
5. Run the relevant tests before opening a PR.

## Credits

Inspired by [Milan Jovanovic](https://www.youtube.com/@MilanJovanovicTech)'s Clean Architecture series.