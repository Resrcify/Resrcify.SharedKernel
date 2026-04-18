# Resrcify.SharedKernel.Caching

`Resrcify.SharedKernel.Caching` provides an `IDistributedCache`-based caching service with typed serialization helpers and simple expiration APIs.

## Table of Contents

- [Resrcify.SharedKernel.Caching](#resrcifysharedkernelcaching)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
    - [Option A: Project reference](#option-a-project-reference)
    - [Option B: NuGet package](#option-b-nuget-package)
  - [Quick Start](#quick-start)
  - [Usage guide](#usage-guide)
    - [Set cache values](#set-cache-values)
    - [Get cache values](#get-cache-values)
    - [Remove values and bulk get](#remove-values-and-bulk-get)
  - [Common issues](#common-issues)
  - [Related modules](#related-modules)

## What you get

- `ICachingService` abstraction in `Resrcify.SharedKernel.Abstractions.Caching`.
- `DistributedCachingService` implementation in `Primitives/`.
- Typed JSON serialization via `System.Text.Json`.
- Support for:
    - Sliding expiration (`TimeSpan`)
    - Absolute expiration (`DateTimeOffset`)
    - Bulk retrieval by keys (`GetBulkAsync<T>`)

## Prerequisites

- .NET 10 SDK.
- A configured `IDistributedCache` provider (memory, Redis, SQL Server, etc.).

## Install

### Option A: Project reference

```xml
<ProjectReference Include="..\path\to\Resrcify.SharedKernel.Caching.csproj" />
```

### Option B: NuGet package

```xml
<PackageReference Include="Resrcify.SharedKernel.Caching" Version="<latest>" />
```

CLI:

```powershell
dotnet add package Resrcify.SharedKernel.Caching
```

## Quick Start

```csharp
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Caching.Primitives;

public static class CachingRegistration
{
    public static IServiceCollection AddCaching(
        this IServiceCollection services)
    {
        services.AddDistributedMemoryCache();
        services.AddSingleton<ICachingService, DistributedCachingService>();
        return services;
    }
}
```

## Usage guide

### Set cache values

Sliding expiration:

```csharp
await cachingService.SetAsync(
    key: "users:42",
    value: userDto,
    slidingExpiration: TimeSpan.FromMinutes(10),
    cancellationToken: cancellationToken);
```

Absolute expiration:

```csharp
await cachingService.SetAsync(
    key: "users:42",
    value: userDto,
    absoluteExpiration: DateTimeOffset.UtcNow.AddMinutes(30),
    cancellationToken: cancellationToken);
```

### Get cache values

```csharp
UserDto? cachedUser = await cachingService.GetAsync<UserDto>(
    key: "users:42",
    cancellationToken: cancellationToken);
```

### Remove values and bulk get

```csharp
await cachingService.RemoveAsync(
    key: "users:42",
    cancellationToken: cancellationToken);

IEnumerable<UserDto?> cachedUsers = await cachingService.GetBulkAsync<UserDto>(
    keys: ["users:1", "users:2", "users:3"],
    cancellationToken: cancellationToken);
```

## Common issues

- If cache entries never expire, verify the provider supports requested expiration settings.
- If deserialization fails, ensure cached payload schema matches the expected type.
- If performance degrades on bulk operations, review provider latency and key count per request.

## Related modules

- `Resrcify.SharedKernel.Messaging` includes `CachingPipelineBehavior<TRequest, TResponse>` for cache-aside query handling.