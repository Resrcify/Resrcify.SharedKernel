# Resrcify.SharedKernel.Repository

`Resrcify.SharedKernel.Repository` provides repository primitives and specification-based query support for aggregate roots.

## Table of Contents

- [Resrcify.SharedKernel.Repository](#resrcifysharedkernelrepository)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
    - [Option A: Project reference](#option-a-project-reference)
    - [Option B: NuGet package](#option-b-nuget-package)
  - [Quick Start](#quick-start)
  - [Usage guide](#usage-guide)
    - [Repository contract](#repository-contract)
    - [Repository implementation](#repository-implementation)
    - [Specification usage](#specification-usage)
  - [Common issues](#common-issues)
  - [Sample project](#sample-project)

## What you get

- Repository abstractions in `Resrcify.SharedKernel.Abstractions.Repository`.
- Base repository primitives in `Primitives/`.
- Specification pattern support through:
    - `Specification<TEntity, TId>`
    - `SpecificationEvaluator`

## Prerequisites

- .NET 10 SDK.
- Entity Framework Core in your consuming project.

## Install

### Option A: Project reference

```xml
<ProjectReference Include="..\path\to\Resrcify.SharedKernel.Repository.csproj" />
```

### Option B: NuGet package

```xml
<PackageReference Include="Resrcify.SharedKernel.Repository" Version="<latest>" />
```

CLI:

```powershell
dotnet add package Resrcify.SharedKernel.Repository
```

## Quick Start

Register your concrete repository type in DI:

```csharp
services.AddScoped<ICompanyRepository, CompanyRepository>();
```

## Usage guide

### Repository contract

`IRepository<TEntity, TId>` has what every repository needs: `GetByIdAsync` and `FirstOrDefaultAsync` (returning
the aggregate, or `null`), `GetAllAsync`, `FindAsync`, `ExistsAsync`, `AddAsync`, `Remove`. A per-aggregate
interface derives from it and adds only the queries its handlers need:

```csharp
using Resrcify.SharedKernel.Abstractions.Repository;

public interface ICompanyRepository
    : IRepository<Company, CompanyId>
{
    Task<Company?> GetCompanyWithContactsAsync(
        CompanyId companyId,
        CancellationToken cancellationToken = default);
}
```

### Repository implementation

```csharp
internal sealed class CompanyRepository(
    AppDbContext context)
    : Repository<AppDbContext, Company, CompanyId>(context), ICompanyRepository
{
    public Task<Company?> GetCompanyWithContactsAsync(
        CompanyId companyId,
        CancellationToken cancellationToken = default)
        => Context.Companies
            .Include(x => x.Contacts)
            .FirstOrDefaultAsync(x => x.Id == companyId, cancellationToken);
}
```

### Not found is the handler's decision

A fetch returns `null` when there is nothing; whether that is an error depends on the use case (a 404 for "get",
the normal path for "create if missing"). The handler turns it into its own error:

```csharp
var company = await companies
    .GetCompanyWithContactsAsync(command.CompanyId, cancellationToken)
    .ToResultAsync(() => DomainErrors.Company.NotFound(command.CompanyId.Value));
if (company.IsFailure)
    return company;
```

`ToResultAsync(error)` takes the error itself; `ToResultAsync(() => error)` makes it only when nothing was found,
which saves formatting its message on every successful fetch. `ToResult` does the same for a value you already
have.

### Specification usage

```csharp
var specification = new ActiveCompaniesSpecification();

IEnumerable<Company> companies = await repository.FindAsync(
    specification,
    cancellationToken);
```

## Common issues

- If includes/order clauses are ignored, verify they are defined on the specification and passed through evaluator.
- If generic constraints fail, ensure entity types implement the expected aggregate-root contracts.
- If queries are slow, evaluate specification complexity and EF tracking settings.

## Sample project

See `samples/Resrcify.SharedKernel.WebApiExample` for repository and specification usage in an application flow.