# Resrcify.SharedKernel.Web

`Resrcify.SharedKernel.Web` provides helpers for converting `Result`/`Result<T>` outcomes into HTTP responses and standardized problem details.

## Table of Contents

- [Resrcify.SharedKernel.Web](#resrcifysharedkernelweb)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
    - [Option A: Project reference](#option-a-project-reference)
    - [Option B: NuGet package](#option-b-nuget-package)
  - [Quick Start](#quick-start)
  - [Usage guide](#usage-guide)
    - [Convert result to problem details](#convert-result-to-problem-details)
    - [Use Match in controllers](#use-match-in-controllers)
    - [Functional endpoint flow](#functional-endpoint-flow)
  - [Common issues](#common-issues)
  - [Sample project](#sample-project)

## What you get

- `ApiController` base type in `Primitives/`.
- Result-to-HTTP conversion extensions in `Extensions/`.
- Consistent mapping from `ErrorType` to HTTP problem responses.

## Prerequisites

- .NET 10 SDK.
- ASP.NET Core dependencies in the consuming application.
- `Resrcify.SharedKernel.Results` and `Resrcify.SharedKernel.Messaging` referenced by the consuming application.

## Install

### Option A: Project reference

```xml
<ProjectReference Include="..\path\to\Resrcify.SharedKernel.Web.csproj" />
```

### Option B: NuGet package

```xml
<PackageReference Include="Resrcify.SharedKernel.Web" Version="<latest>" />
```

CLI:

```powershell
dotnet add package Resrcify.SharedKernel.Web
```

## Quick Start

```csharp
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;

services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
```

## Usage guide

### Convert result to problem details

```csharp
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Extensions;

Result result = await SomeOperationAsync();

if (result.IsFailure)
{
    return result.ToProblemDetails();
}

return Results.Ok();
```

### Use Match in controllers

```csharp
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Primitives;

[Route("api/company")]
public sealed class CompanyController(
    ISender sender)
    : ApiController(sender)
{
    [HttpGet("{id:guid}")]
    public async Task<IResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        Result<CompanyDto> result = await Sender.Send(new GetCompanyByIdQuery(id), cancellationToken);

        return result.Match(
            onSuccess: Results.Ok,
            onFailure: ToProblemDetails);
    }
}
```

### Functional endpoint flow

```csharp
return await Result
    .Create(new GetCompanyByIdQuery(id))
    .Bind(request => Sender.Send(request, cancellationToken))
    .Match(
        onSuccess: Results.Ok,
        onFailure: ToProblemDetails);
```

## Common issues

- If response serialization differs between minimal APIs and controllers, ensure both `JsonOptions` and MVC JSON options are configured.
- If status codes look wrong, verify error classification (`ErrorType`) at the source.

## Sample project

See `samples/Resrcify.SharedKernel.WebApiExample` for practical usage in controllers and endpoint flows.