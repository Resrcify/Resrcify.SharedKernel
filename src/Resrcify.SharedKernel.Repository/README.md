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

```csharp
using Resrcify.SharedKernel.Abstractions.Repository;
using Resrcify.SharedKernel.Results.Primitives;

public interface ICompanyRepository
    : IRepository<Company, CompanyId>
{
    Task<Result<Company>> GetCompanyAggregateByIdAsync(
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
    public async Task<Result<Company>> GetCompanyAggregateByIdAsync(
        CompanyId companyId,
        CancellationToken cancellationToken = default)
    {
        var company = await Context.Companies
            .Include(x => x.Contacts)
            .FirstOrDefaultAsync(x => x.Id == companyId, cancellationToken);

        return Result.Create(company)
            .Match(
                onSuccess: value => value,
                onFailure: DomainErrors.Company.NotFound(companyId.Value));
    }
}
```

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