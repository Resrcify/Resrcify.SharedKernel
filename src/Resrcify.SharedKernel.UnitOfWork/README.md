# Resrcify.SharedKernel.UnitOfWork

## Description
This repository, **Resrcify.SharedKernel.UnitOfWork**, provides a robust implementation of the Unit of Work pattern for managing transactions and coordinating changes in domain-driven design applications. It encapsulates the transaction management logic, ensuring that all changes to aggregates are handled as a single unit of work. The library is built on top of Entity Framework Core and promotes clean architecture principles. Furthermore the logic for processing domain events with MediatRs Notifications in an Domain-Driven Design setting is also implemented using Quartz.

## Prerequisites
Before using **Resrcify.SharedKernel.UnitOfWork**, ensure that your project meets the following requirements:

- .NET 8.0 is installed.
- The Entity Framework Core package is installed:
  - Entity Framework Core: ``Microsoft.EntityFrameworkCore``
- Please note that all external package references in this repository are private, meaning that you are forced add them to your own project if you need/wish to use them. This is to maintain correct dependency references in accordance with Clean Architecture.

## Installation
To integrate **Resrcify.SharedKernel.UnitOfWork** into your project, you can either clone the source code or install the NuGet package, depending on your preference.

### Download and reference the project files
1. Clone this repository
```bash
git clone https://github.com/Resrcify/Resrcify.SharedKernel.git
```
2. Add the **Resrcify.SharedKernel.UnitOfWork** project to your solution/project.

- By referencing the project in your ``.csproj`` file
    ```xml
    <ProjectReference Include="../path/to/Resrcify.SharedKernel.UnitOfWork.csproj" />
    ```
- Or by using the command line to reference the project
    ```bash
    dotnet add reference path/to/Resrcify.SharedKernel.UnitOfWork.csproj
    ```

### Download and reference Nuget package
1. Add the package from NuGet:
- By referencing in your ``.csproj`` file
    ```xml
    <PackageReference Include="Resrcify.SharedKernel.UnitOfWork" Version="1.8.5" />
    ```
- Or by using the command line
    ```bash
    dotnet add package Resrcify.SharedKernel.UnitOfWork
    ```

## Configuration
To use this library, configure it in your application's startup code or dependency injection setup.
```csharp
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

public void AddPersistanceServices(this IServiceCollection services)
{
    services.AddScoped<IUnitOfWork, UnitOfWork<MyDbContext>>();
}
```

Optionally if you wish to implement processing of domain events using the Quartz library add the following.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
        services.ConfigureOptions<ProcessOutboxMessagesJobSetup<MyDbContext>>();
        return services;
    }
}
```

## Usage
### Saving Changes to the Database
```csharp
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

public class MyService
{
    private readonly IUnitOfWork _unitOfWork;

    public MyService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task CreateAsync(MyEntity entity)
    {
        // Add your entity to the context
        // Perform other operations...

        await _unitOfWork.CompleteAsync();
    }
}
```
### Creating and using a Transactional Scope
```csharp
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

public class MyService
{
    private readonly IUnitOfWork _unitOfWork;

    public MyService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task CreateAsync(MyEntity entity, CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                TimeSpan.FromSeconds(30),
                cancellationToken);

            // Add your entity to the context
            // Perform other operations...
            await _unitOfWork.CompleteAsync();
            await _unitOfWork.CommitTransaction(cancellationToken)
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken)
        }
    }
}
```

## Sample projects
Use of the unit of work can be seen in the sister project **Resrcify.SharedKernel.Messaging** in one of the MediatR Pipeline behaviors. Please see [UnitOfWorkPipelineBehavior.cs](../Resrcify.SharedKernel.Messaging/Behaviors/UnitOfWorkPipelineBehavior.cs) and [TransactionPipelineBehavior.cs](../Resrcify.SharedKernel.Messaging/Behaviors/TransactionPipelineBehavior.cs) for further documentation on how the unit of work can be used in your project(s).

## Suggestions for further development

Here are a few ideas for extending this library in the future:

- **Different Deserialization Option:** Implementing the use of i.e System.Json.Text to deserialize domain events into its correct class.
- **Integration tests using TestContainers:** Adding integration tests using TestContainers to be able to run the test in pipelines. This will further strengthen comfort especially if external dependencies are used.