# Resrcify.SharedKernel.Repository

## Description
This repository, **Resrcify.SharedKernel.Repository**, provides a rflexible implementation of the repository pattern for managing aggregates in domain-driven design applications. It facilitates the separation of concerns by abstracting data access logic and offering an easy-to-use interface for interacting with aggregate roots. The library leverages Entity Framework Core and encourages clean architecture principles.

## Prerequisites
Before using **Resrcify.SharedKernel.Repository**, ensure that your project meets the following requirements:

- .NET 8.0 is installed.
- The Entity Framework Core package is installed:
  - Entity Framework Core: ``Microsoft.EntityFrameworkCore``
- Please note that all external package references in this repository are private, meaning that you are forced add them to your own project if you need/wish to use them. This is to maintain correct dependency references in accordance with Clean Architecture.

## Installation
To integrate **Resrcify.SharedKernel.Repository** into your project, you can either clone the source code or install the NuGet package, depending on your preference.

### Download and reference the project files
1. Clone this repository
```bash
git clone https://github.com/Resrcify/Resrcify.SharedKernel.git
```
2. Add the **Resrcify.SharedKernel.Repository** project to your solution/project.

- By referencing the project in your ``.csproj`` file
    ```xml
    <ProjectReference Include="../path/to/Resrcify.SharedKernel.Repository.csproj" />
    ```
- Or by using the command line to reference the project
    ```bash
    dotnet add reference path/to/Resrcify.SharedKernel.Repository.csproj
    ```

### Download and reference Nuget package
1. Add the package from NuGet:
- By referencing in your ``.csproj`` file
    ```xml
    <PackageReference Include="Resrcify.SharedKernel.Repository" Version="1.8.5" />
    ```
- Or by using the command line
    ```bash
    dotnet add package Resrcify.SharedKernel.Repository
    ```

## Configuration
To use the repository service, configure it in your application's startup code or dependency injection setup.
```csharp
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Repository.Abstractions;

public void AddPersistanceServices(this IServiceCollection services)
{
    services.AddScoped<ICompanyRepository, CompanyRepository>();
}
```
Each specific repository needs to inherent from the IRepository interface.

```csharp
public interface ICompanyRepository
    : IRepository<Company, CompanyId>
{
    Task<Result<Company>> GetCompanyAggregateByIdAsync(
        CompanyId companyId,
        CancellationToken cancellationToken = default);
}
```
Which in turn inherents from the predefined abstract Repository class.
```csharp
internal sealed class CompanyRepository(AppDbContext context)
     : Repository<AppDbContext, Company, CompanyId>(context),
        ICompanyRepository
{
   public async Task<Result<Company>> GetCompanyAggregateByIdAsync(
      CompanyId companyId,
      CancellationToken cancellationToken = default)
      => Result
         .Create(
            await Context.Companies
               .Include(x => x.Contacts)
               .FirstOrDefaultAsync(x => x.Id == companyId, cancellationToken))
         .Match(
            company => company,
            DomainErrors.Company.NotFound(companyId.Value));
}
```

## Usage
### Retrieving an Entity by Id
```csharp
var entity = await repository.GetByIdAsync(entityId, cancellationToken);
```
### Retrieving the First Entity Matching a Predicate
```csharp
var entity = await repository.FirstOrDefaultAsync(e => e.Property == value, cancellationToken);
```
### Retrieving All Entities
```csharp
var allEntities = await repository.GetAllAsync(cancellationToken);
```
### Finding Entities Based on a Specification
```csharp
var specification = new MySpecification();
var entities = await repository.FindAsync(specification, cancellationToken);
```
### Adding a New Entity
```csharp
await repository.AddAsync(newEntity, cancellationToken);
```
### Removing an Entity
```csharp
repository.Remove(existingEntity);
```
### Bulk Operations
```csharp
await repository.AddRangeAsync(new List<TEntity> { entity1, entity2 }, cancellationToken);
repository.RemoveRange(new List<TEntity> { entity1, entity2 });
```
For a complete set of methods, please see the repository.
### Specifications
Specifications in **Resrcify.SharedKernel.Repository** provide a way to encapsulate query logic, allowing for cleaner and more maintainable code. They enable complex querying by combining various criteria, includes, and sorting options.

- Specification<TEntity, TId>: Base class for defining specifications that can be used to filter and include related entities in queries.
- SpecificationEvaluator: Responsible for applying the specifications to the queryable data source, handling includes, ordering, and other query parameters.

By using specifications, developers can easily create reusable query definitions, improving the maintainability of their data access logic.

## Sample projects
Use of the repository library has been showcased in the sample project [**Resrcify.SharedKernel.WebApiExample**](../../samples/Resrcify.SharedKernel.WebApiExample).

## Suggestions for further development

Here are a few ideas for extending this library in the future:

- **Expand the specification:** Implementing additional logic in the Specification.
- **Implement optional type safety:** Use type constraint to optionally make the Repository pattern tie into Domain-Driven Design by forcing a ValueObject as an Id.