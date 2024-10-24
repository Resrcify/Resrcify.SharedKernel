# Resrcify.SharedKernel.Messaging

## Description
This repository, **Resrcify.SharedKernel.Messaging**, was created easy implementation of messaging patterns in applications. By leveraging MediatR and other custom messaging abstractions, this library simplifies the process of sending commands and queries while maintaining clean architecture principles and promoting separation of concerns.

## Prerequisites
Before using **Resrcify.SharedKernel.Messaging**, ensure that your project meets the following requirements:

- .NET 8.0 is installed.
- The MediatR package is installed:
    - MediatR: ``MediatR``
- Please note that all external package references in this repository are private, meaning that you are forced add them to your own project if you need/wish to use them. This is to maintain correct dependency references in accordance with Clean Architecture.

## Installation
To integrate **Resrcify.SharedKernel.Messaging** into your project, you can either clone the source code or install the NuGet package, depending on your preference.

### Download and reference the project files
1. Clone this repository
```bash
git clone https://github.com/Resrcify/Resrcify.SharedKernel.git
```
2. Add the **Resrcify.SharedKernel.Messaging** project to your solution/project.

- By referencing the project in your ``.csproj`` file
    ```xml
    <ProjectReference Include="../path/to/Resrcify.SharedKernel.Messaging.csproj" />
    ```
- Or by using the command line to reference the project
    ```bash
    dotnet add reference path/to/Resrcify.SharedKernel.Messaging.csproj
    ```

### Download and reference Nuget package
1. Add the package from NuGet:
- By referencing in your ``.csproj`` file
    ```xml
    <PackageReference Include="Resrcify.SharedKernel.Messaging" Version="1.8.5" />
    ```
- Or by using the command line
    ```bash
    dotnet add package Resrcify.SharedKernel.Messaging
    ```

## Configuration
To use this library, configure it in your application's startup code or dependency injection setup.
```csharp
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Messaging.Abstractions;

public void AddApplicationServices(this IServiceCollection services)
{
    services.AddMediatR(config =>
    {
        config.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly());
        config.AddOpenBehavior(typeof(LoggingPipelineBehavior<,>));
        config.AddOpenBehavior(typeof(UnitOfWorkPipelineBehavior<,>));
        config.AddOpenBehavior(typeof(CachingPipelineBehavior<,>));
    });
}
```
- Configure assembly scanning to scan and register MediatR and wire upp the messaging classes.
- Add the wanted behaviors using "AddOpenBehavior" to customize the needed functionality for your application.

    Currently the following behaviors are available:
    - CachingPipelineBehavior
    - LoggingPipelineBehavior
    - TransactionPipelineBehavior
    - UnitOfWorkPipelineBehavior
    - ValidationPipelineBehavior

## Usage
### Sending a Query
```csharp
IQuery query = new MyQuery();
var result = await mediator.Send(query, cancellationToken);
```
### Sending a CachingQuery
```csharp
ICachingQuery query = new MyQuery();
var result = await mediator.Send(query, cancellationToken);
```
### Sending a Command
```csharp
ICommand command = new MyCommand();
var result = await mediator.Send(command, cancellationToken);
```
### Sending a TransactionCommand
```csharp
ITransactionCommand command = new MyCommand();
var result = await mediator.Send(command, cancellationToken);
```
### Behaviors
Behaviors in **Resrcify.SharedKernel.Messaging** extend the capabilities of MediatR by allowing cross-cutting concerns to be implemented. It is relying on the custom implementations of the abstractions Query and Command and by defining specific sub-requests types different behaviors can be triggered:

- LoggingPipelineBehavior: Implementing logging of messages sent and received, along with execution times for each handler. Doesn't require a specific request type, but any custom type or the native request type can be used.
- ValidationPipelineBehavior: Adding validation logic using ``FluentValidation`` before processing a command or query, ensuring that all required data is present and valid. Doesn't require a specific request type, but any custom type or the native request type can be used.
- Transaction Management: Relying on ``EntityFrameworkCore`` managing transactions to ensure that commands are executed within a single transaction scope, rolling back changes if necessary. Requires the use of the ITransactionCommand abstraction in the message.
- UnitOfWorkPipelineBehavior: Also relying on ``EntityFrameworkCore`` but for handling the SaveChanges operation after every command handler has processed successfully. Requires the use of the ICommand abstraction in the message.
- CachingPipelineBehavior: Implementing caching strategies for queries to reduce redundant processing and improve performance. Requires the use of the ICachingQuery abstraction in the message.

## Sample projects
Extensive use of the messaging library has been showcased in the sample project [**Resrcify.SharedKernel.WebApiExample**](../../samples/Resrcify.SharedKernel.WebApiExample).

## Suggestions for further development

Here are a few ideas for extending this library in the future:

- **Support for Additional Messaging Patterns:** Implementing additional messaging patterns, such as event sourcing or publish-subscribe models.
- **Integration with Message Queues:** Adding support for integration with message queue systems like RabbitMQ or Azure Service Bus.
- **Enhanced Error Handling:** Implementing enhanced error handling and retry mechanisms for messaging operations.