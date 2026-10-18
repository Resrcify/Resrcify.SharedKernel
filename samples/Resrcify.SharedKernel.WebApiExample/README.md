# Resrcify.SharedKernel.WebApiExample

`Resrcify.SharedKernel.WebApiExample` is the canonical SharedKernel 4.3 service: a small company/contact API wired the
way every Titan service is, built and tested with SharedKernel's own solution (so a breaking change shows here first).

## Table of Contents

- [What it shows](#what-it-shows)
- [The host](#the-host)
- [Layers](#layers)
- [Run the sample](#run-the-sample)
- [Endpoints](#endpoints)
- [Tests](#tests)

## What it shows

- **Minimal APIs** on the mediator: one `IEndpoint` per use case (`Presentation/Endpoints/Companies/`), each a
  `MapPostRequest` / `MapGetRequest` / ... that turns the HTTP request into a command or query and answers its
  `Result` (200 or 204, or problem details). No controllers, no Swagger: OpenAPI at `/openapi/v1.json` in Development.
- **The mediator**: `AddMediator(cfg => cfg.RegisterServicesFromAssemblies(...).AddStandardBehaviors())`, which also
  registers the FluentValidation validators (no `AddValidatorsFromAssembly`); cached queries (`ICachingQuery`).
- **Telemetry**: `AddServiceTelemetry` (OpenTelemetry, Prometheus at `/metrics`, OTLP when
  `Observability:OtlpEndpoint` is set, Serilog from the `Serilog` section, Npgsql's spans) and
  `UseServiceDefaults` (one line per request, then `UseExceptionHandler()` inside it, in one call).
- **Problem details** for failed results, unhandled exceptions and unbindable requests (`AddResultProblemDetails`).
- **PostgreSQL and the outbox**: `AddPostgresDbContext` from the `Database` section with `WithOutbox()` (its claim makes
  several instances safe) and `WithOutboxWakeUp()`, `AddOutboxProcessing` on Quartz, migrations on start-up.
- **Caching**: `AddDistributedCaching()` (the cache, `ICachingService` and `IClaimStore` in one call).
- **Another service**: a typed `HttpClient` with `AddResultResilience("OrganizationRegistry:Resilience")`, its retries
  and timeouts from configuration, validated when the host starts (`Infrastructure/OrganizationRegistry/`).
- **Health checks** on `/health`, `/health/ready` (PostgreSQL, the outbox) and `/health/live` (the Quartz jobs), each
  tagged by SharedKernel's defaults.
- **The clock**: every SharedKernel registration reads the container's `TimeProvider`; the host registers none.

## The host

`src/Resrcify.SharedKernel.WebApiExample.Web/Program.cs`, the whole of it:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddPresentationServices()
    .AddApplicationServices()
    .AddInfrastructureServices(builder.Configuration)
    .AddPersistenceServices(builder.Configuration);

var app = builder.Build();
app.UseServiceDefaults();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapEndpoints();
app.MapHealthEndpoints();
app.MapServiceMetrics();
await app.RunAsync();
```

## Layers

Each layer references only the SharedKernel packages the layer table in the repository's README allows it (the
architecture tests check the code):

| Project | SharedKernel packages |
|---|---|
| Domain | DomainDrivenDesign, Results |
| Application | Mediator (Abstractions, Results) |
| Persistence | Repository, UnitOfWork, UnitOfWork.Postgres |
| Infrastructure | Caching, Observability, UnitOfWork (Quartz heartbeats), Web (`AddResultResilience`) |
| Presentation | Web (endpoints, problem details) |
| Web | the layers above |

## Run the sample

From this folder:

```powershell
docker-compose up --build
```

It builds from the repository root (the sample references the SharedKernel projects) and runs PostgreSQL 18 next to
it. Or run it locally against a database of your own: set `Database:Host` / `Database:Port` (e.g. as `Database__Host`
environment variables) and `dotnet run` the Web project.

## Endpoints

| Method | Route | Request |
|---|---|---|
| `POST` | `api/companies` | `CreateCompanyCommand` |
| `GET` | `api/companies` | `GetAllCompaniesQuery` (cached 5 min) |
| `GET` | `api/companies/{companyId}` | `GetCompanyByIdQuery` (cached 5 min) |
| `PATCH` | `api/companies/{companyId}` | `UpdateCompanyNameCommand` |
| `POST` | `api/companies/{companyId}/contacts` | `AddContactCommand` |
| `PATCH` | `api/companies/{companyId}/contacts` | `UpdateContactByEmailCommand` |
| `DELETE` | `api/companies/{companyId}/contacts` | `RemoveContactCommand` |

Creating a company and renaming one raise domain events, which the outbox hands to their handlers after the save
(woken by `NOTIFY`, or at the next 20 s poll).

## Tests

- `tests/Resrcify.SharedKernel.WebApiExample.Domain.UnitTests`: the aggregate and its value objects.
- `tests/Resrcify.SharedKernel.WebApiExample.ArchitectureTests`: SharedKernel's conventional rules (layers, which
  layer uses which package, endpoint and handler conventions), inherited as they are.

The images under `images/` are from the 3.x sample (controllers and Swagger), kept for the record.
