# Resrcify.SharedKernel.WebApiExample

`Resrcify.SharedKernel.WebApiExample` is a sample ASP.NET Core API that demonstrates how SharedKernel modules work together in a CRM-like company/contact domain.

## Table of Contents

- [Resrcify.SharedKernel.WebApiExample](#resrcifysharedkernelwebapiexample)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Run the sample](#run-the-sample)
  - [Feature coverage](#feature-coverage)
    - [Company lifecycle](#company-lifecycle)
    - [Contact lifecycle](#contact-lifecycle)
    - [Query endpoints](#query-endpoints)
  - [Non-functional goals](#non-functional-goals)
  - [Testing overview](#testing-overview)

## What you get

- A runnable API using SharedKernel modules as NuGet dependencies.
- Example usage for:
    - `Messaging`
    - `Results`
    - `DomainDrivenDesign`
    - `Repository`
    - `UnitOfWork`
    - `Caching`
    - `Web`
- Swagger UI for manual endpoint testing.

## Prerequisites

- Docker and Docker Compose.
- .NET 10 SDK (for local builds/tests).

## Run the sample

```powershell
Set-Location "d:\Google Drive\Projects\Titan404\Resrcify.SharedKernel\samples\Resrcify.SharedKernel.WebApiExample"
docker-compose up --build
```

Swagger:

- `http://localhost:11000/swagger/index.html`

## Feature coverage

### Company lifecycle

- Create company with validation and duplicate checks.
- Update company name with domain event emission when changed.
- Publish notifications for company creation and rename.

### Contact lifecycle

- Add contact with uniqueness and format validation.
- Update contact by email.
- Remove contact by email.

### Query endpoints

- Get all companies.
- Get company by id.
- Cache-aside behavior for query endpoints with expiration.

## Non-functional goals

- Clean Architecture boundaries.
- Domain-Driven Design primitives and aggregate rules.
- Result-pattern flow (avoid exception-driven control flow).
- Query caching for read-heavy endpoints.

## Testing overview

- Unit tests for company and contact command/query logic.
- Architecture tests for layering constraints.
- Manual Swagger verification with database/log inspection.

Evidence images are available under `images/` in this sample project.