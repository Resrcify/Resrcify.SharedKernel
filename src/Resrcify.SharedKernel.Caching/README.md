# Resrcify.SharedKernel.Caching

## Description
This repository, **Resrcify.SharedKernel.Caching**, was created to simplify caching operations in our distributed applications. By leveraging the ``IDistributedCache`` interface, this library makes it easier to implement effective caching strategies that enhance performance and minimize unnecessary database queries.

## Prerequisites
Before using **Resrcify.SharedKernel.Caching**, ensure that your project meets the following requirements:

- .NET 8.0 is installed.
- Installing the nuget of a distributed cache provider, such as:
    - In-Memory Cache: ``Microsoft.Extensions.Hosting``

    As of now other providers are not supported.
- Please note that all external package references in this repository are private, meaning that you are forced add them to your own project if you need/wish to use them. This is to maintain correct dependency references in accordance with Clean Architecture.

## Installation
To integrate **Resrcify.SharedKernel.Caching** into your project, you can either clone the source code or install the NuGet package, depending on your preference.

### Download and reference the project files
1. Clone this repository
```bash
git clone https://github.com/Resrcify/Resrcify.SharedKernel.git
```
2. Add the **Resrcify.SharedKernel.Caching** project to your solution/project.

- By referencing the project in your ``.csproj`` file
    ```xml
    <ProjectReference Include="../path/to/Resrcify.SharedKernel.Caching.csproj" />
    ```
- Or by using the command line to reference the project
    ```bash
    dotnet add reference path/to/Resrcify.SharedKernel.Caching.csproj
    ```

### Download and reference Nuget package
1. Add the package from NuGet:
- By referencing in your ``.csproj`` file
    ```xml
    <PackageReference Include="Resrcify.SharedKernel.Caching" Version="1.8.5" />
    ```
- Or by using the command line
    ```bash
    dotnet add package Resrcify.SharedKernel.Caching
    ```

## Configuration
To use the caching service, configure it in your application's startup code or dependency injection setup.

Example:
```csharp
using Resrcify.SharedKernel.Caching.Abstractions;
using Resrcify.SharedKernel.Caching.Primitives;
using Microsoft.Extensions.DependencyInjection;

public void AddInfrastructureServices(this IServiceCollection services)
{
    services.AddDistributedMemoryCache();
    services.AddSingleton<ICachingService, InMemoryCachingService>();
}
```

## Usage
Some of the more commonly used methods can be used as follows:
### Add to cache
```csharp
MyClass value = new MyClass("test");
var validFor = TimeSpan.FromMinutes(30);
await cachingService.SetAsync(
    "cacheKey",
    value,
    validFor,
    cancellationToken);
```
### Retreive from cache
```csharp
MyClass? cacheResult = await _cachingService.GetAsync<MyClass>(
    "cacheKey",,
    cancellationToken);
```
## Sample projects
Caching is used within the sister project **Resrcify.SharedKernel.Messaging** in one of the MediatR Pipeline behaviors. Please see [CachingPipelineBehavior.cs](../Resrcify.SharedKernel.Messaging/Behaviors/CachingPipelineBehavior.cs) for further documentation on how caching can be used in your project(s).

## Suggestions for further development

Here are a few ideas for extending this library in the future:

- **Custom Cache Invalidation Strategies:** Implementing additional or custom cache invalidation policies, such as sliding window expiration or similar.
- **Additional Distributed Cache Providers:** Adding support for other custom distributed cache providers, i.e for Redis Cache or SQL Server.
- **Monitoring and Metrics:** Add instrumentation to monitor cache hits, misses, and other metrics using telemetry or logging frameworks.
- **Serialization Customization:** Allow customization of serialization (e.g., adding support for Newtonsoft.Json, BSON or other custom formats).
- **Support for Compression:** Adding optional compression for cached data to reduce memory usage.
- **Integration tests using TestContainers:** Adding integration tests using TestContainers to be able to run the test in pipelines. This will further strengthen comfort especially if external dependencies are used.