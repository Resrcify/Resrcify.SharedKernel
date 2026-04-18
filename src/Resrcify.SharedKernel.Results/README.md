# Resrcify.SharedKernel.Results

`Resrcify.SharedKernel.Results` provides a consistent Result pattern for success/failure flows, typed errors, and functional-style composition.

## Table of Contents

- [Resrcify.SharedKernel.Results](#resrcifysharedkernelresults)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
    - [Option A: Project reference](#option-a-project-reference)
    - [Option B: NuGet package](#option-b-nuget-package)
  - [Quick Start](#quick-start)
  - [Usage guide](#usage-guide)
    - [Basic result flow](#basic-result-flow)
    - [Combining results](#combining-results)
    - [Functional chaining](#functional-chaining)
  - [Common issues](#common-issues)
  - [Sample project](#sample-project)

## What you get

- Core primitives in `Primitives/`:
    - `Result`
    - `Result<TValue>`
    - `Error`
    - `ErrorType`
- Factory and composition helpers:
    - `Success(...)`, `Failure(...)`, `Create(...)`
    - `Combine(...)`
    - `Map(...)`, `Bind(...)`, `Tap(...)`, `Ensure(...)`, `Match(...)`
- Sync and async extension methods for fluent pipelines.

## Prerequisites

- .NET 10 SDK.

## Install

### Option A: Project reference

```xml
<ProjectReference Include="..\path\to\Resrcify.SharedKernel.Results.csproj" />
```

### Option B: NuGet package

```xml
<PackageReference Include="Resrcify.SharedKernel.Results" Version="<latest>" />
```

CLI:

```powershell
dotnet add package Resrcify.SharedKernel.Results
```

## Quick Start

```csharp
using Resrcify.SharedKernel.Results.Primitives;

public static Result<UserDto> GetUser(
    string userId)
{
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Error.Validation(
            "User.InvalidId",
            "User id cannot be empty.");
    }

    UserDto? user = FindUser(userId);

    return user is null
        ? Error.NotFound("User.NotFound", $"User '{userId}' was not found.")
        : Result.Success(user);
}
```

## Usage guide

### Basic result flow

```csharp
Result<UserDto> result = GetUser(userId);

if (result.IsSuccess)
{
    return Result.Success(result.Value);
}

foreach (Error error in result.Errors)
{
    logger.LogWarning("{Code}: {Message}", error.Code, error.Message);
}

return Result.Failure<UserDto>(result.Errors);
```

### Combining results

```csharp
Result<UserDto> user = GetUser(userId);
Result<List<OrderDto>> orders = GetOrders(userId);

Result<UserProfileDto> profile = Result.Combine(user, orders)
    .Map(tuple => new UserProfileDto(tuple.Item1, tuple.Item2));
```

### Functional chaining

```csharp
Result<UserProfileDto> profile = Result.Combine(user, orders)
    .Map(tuple => new UserProfileDto(tuple.Item1, tuple.Item2))
    .Ensure(
        value => value.Orders.Count > 0,
        Error.Validation("Profile.EmptyOrders", "Orders cannot be empty."))
    .Tap(value => audit.Log("profile-created", value.User.Id));
```

## Common issues

- If `.Value` throws, check `IsSuccess` first.
- If combined results lose detail, ensure source errors use meaningful codes/messages.
- If async chains are hard to read, split into intermediate `Result<T>` values for clarity.

## Sample project

See `samples/Resrcify.SharedKernel.WebApiExample` for end-to-end usage with messaging, repository, and web layers.