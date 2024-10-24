# Resrcify.SharedKernel.Web

## Description
The **Resrcify.SharedKernel.Web** library provides essential extensions for handling HTTP responses and converting application results into problem details. This library streamlines error handling by transforming Result objects into standardized problem responses suitable for use in ASP.NET Core applications.

## Prerequisites
Before using **Resrcify.SharedKernel.Web**, ensure that your project meets the following requirements:

- .NET 8.0 is installed.
- The MediatR package is installed:
    - MediatR: ``MediatR``
- The ASP.NET Core framework is installed:
    - Microsoft.AspNetCore.Mvc: ``Microsoft.AspNetCore.Mvc``
- Please note that all external package references in this repository are private, meaning that you are forced add them to your own project if you need/wish to use them. This is to maintain correct dependency references in accordance with Clean Architecture.

## Installation
To integrate **Resrcify.SharedKernel.Web** into your project, you can either clone the source code or install the NuGet package, depending on your preference.

### Download and reference the project files
1. Clone this repository
```bash
git clone https://github.com/Resrcify/Resrcify.SharedKernel.git
```
2. Add the **Resrcify.SharedKernel.Web** project to your solution/project.

- By referencing the project in your ``.csproj`` file
    ```xml
    <ProjectReference Include="../path/to/Resrcify.SharedKernel.Web.csproj" />
    ```
- Or by using the command line to reference the project
    ```bash
    dotnet add reference path/to/Resrcify.SharedKernel.Web.csproj
    ```

### Download and reference Nuget package
1. Add the package from NuGet:
- By referencing in your ``.csproj`` file
    ```xml
    <PackageReference Include="Resrcify.SharedKernel.Web" Version="1.8.5" />
    ```
- Or by using the command line
    ```bash
    dotnet add package Resrcify.SharedKernel.Web
    ```

## Configuration
To use this library, configure it in your application's startup code or dependency injection setup.
```csharp
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Resrcify.SharedKernel.WebApiExample.Presentation;

public static class PresentationServiceRegistration
{
    public static IServiceCollection AddPresentationServices(this IServiceCollection services)
    {
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals;
        });

        services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals;
            });

        return services;
    }
}
```
Please note that it is nessecary to configure serialization in both JsonOptions and the Controller since when using an IResult in a non-minimal api setting the controller outward serialization is ignored.

## Usage
### Converting Results to Problem Details
```csharp
using MediatR;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Primitives;

[Route("api/myroute")]
public class MyController(ISender sender) : ApiController(sender)
{
    [HttpGet]
    public async Task<IResult> Get()
    {
        Result result = await SomeOperationAsync();
        if(result.IsFailure)
            return result.ToProblemDetails();
        return Results.Ok(result.Value);
    }
}
```
### Matching Result with Callbacks
```csharp
using MediatR;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Primitives;

[Route("api/myroute")]
public class MyController(ISender sender) : ApiController(sender)
{
    [HttpGet]
    public async Task<IResult> Get()
    {
        Result result = await SomeOperationAsync();
        return result.Match(
            Results.Ok,
            ToProblemDetails);
    }
}
```
### Utilizing Functional Programming
```csharp
using MediatR;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Primitives;

[Route("api/myroute")]
public class MyController(ISender sender) : ApiController(sender)
{
    [HttpGet()]
    public async Task<IResult> Get(
        CancellationToken cancellationToken = default)
        => await Result
            .Create(new MediatRQuery())
            .Bind(request => Sender.Send(request, cancellationToken))
            .Match(Results.Ok, ToProblemDetails);
}
```
## Sample projects
Extensive use of the libraty has been showcased in the sample project [**Resrcify.SharedKernel.WebApiExample**](../../samples/Resrcify.SharedKernel.WebApiExample).

## Suggestions for further development

Here are a few ideas for extending this library in the future:

- **Custom Problem Details Extensions:** Allowing developers to easily add custom properties to the problem details responses for richer error context.