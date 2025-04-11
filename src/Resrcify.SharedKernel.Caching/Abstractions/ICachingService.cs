using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace Resrcify.SharedKernel.Caching.Abstractions;

public interface ICachingService
{
    Task<T?> GetAsync<T>(
        string key,
        JsonSerializerOptions? serializerOptions,
        CancellationToken cancellationToken = default)
        where T : class;


    Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
        where T : class;

    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan slidingExpiration,
        CancellationToken cancellationToken = default)
        where T : class;

    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan slidingExpiration,
        JsonSerializerOptions? serializerOptions,
        CancellationToken cancellationToken = default)
        where T : class;

    Task SetAsync<T>(
       string key,
       T value,
       DistributedCacheEntryOptions cacheOptions,
       JsonSerializerOptions? serializerOptions,
       CancellationToken cancellationToken = default)
       where T : class;

    Task SetAsync<T>(
        string key,
        T value,
        DistributedCacheEntryOptions cacheOptions,
        CancellationToken cancellationToken = default)
        where T : class;

    Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<T?>> GetBulkAsync<T>(
        IEnumerable<string> keys,
        JsonSerializerOptions? serializerOptions,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<T?>> GetBulkAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default);
}