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
    - [Claim a key (do something once)](#claim-a-key-do-something-once)
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
    - Claims (`TryClaimForAsync`): set a key only if nobody holds it
- Values serialized with the caller's `JsonSerializerOptions` (indentation and escaping too).

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

Every entry expires, and how long it lives is in the method's name:

| Call | The entry is kept |
|---|---|
| `SetForAsync(key, value, TimeSpan expiresIn)` | for `expiresIn` from now; reads don't extend it |
| `SetAsync(key, value, DateTimeOffset absoluteExpiration)` | until that time |
| `SetSlidingAsync(key, value, TimeSpan slidingExpiration)` | while it is read at least every `slidingExpiration` |

```csharp
// A one-time code: gone after 5 minutes, however often it is read.
await cachingService.SetForAsync($"otc:{email}", code, TimeSpan.FromMinutes(5), cancellationToken);

// A user profile: kept while it is in use.
await cachingService.SetSlidingAsync("users:42", userDto, TimeSpan.FromMinutes(10), cancellationToken);
```

An implementation provides one method, `SetAsync(key, value, absoluteExpiration, slidingExpiration,
serializerOptions, cancellationToken)`; the others call it. It refuses an entry with neither expiration: one that
never expires stays until the cache runs out of memory. Data kept until the next update replaces it gets a lifetime
longer than the update interval, and its readers handle it being gone.

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

### Claim a key (do something once)

```csharp
if (await cachingService.TryClaimForAsync("payouts:sent:42", TimeSpan.FromHours(3), cancellationToken))
{
    // this caller holds the claim; release it with RemoveAsync if the work must be retried
}
```

A claim is a marker, not a value: use a claim key only with `TryClaimForAsync` and `RemoveAsync`, never with
`GetAsync`/`SetAsync`. `DistributedCachingService` claims atomically **within one process** only (`IDistributedCache`
can't set a key only if it is absent): two instances sharing a cache can both claim a key. An `ICachingService` over
Redis can claim across processes with `SET key value PX <ms> NX`.

## Common issues

- If cache entries never expire, verify the provider supports requested expiration settings.
- If deserialization fails, ensure cached payload schema matches the expected type.
- If performance degrades on bulk operations, review provider latency and key count per request.

## Related modules

- `Resrcify.SharedKernel.Mediator` includes `CachingPipelineBehavior<TRequest, TResponse>` for cache-aside query handling.