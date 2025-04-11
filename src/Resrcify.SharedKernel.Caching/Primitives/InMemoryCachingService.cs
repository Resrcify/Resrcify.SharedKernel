using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Text.Json;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.Caching.Abstractions;

namespace Resrcify.SharedKernel.Caching.Primitives;


public sealed class InMemoryCachingService : ICachingService
{
    private readonly IDistributedCache _distributedCache;

    public InMemoryCachingService(IDistributedCache distributedCache)
        => _distributedCache = distributedCache;

    public async Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
        where T : class
        => await GetAsync<T>(
            key,
            null,
            cancellationToken);
    public async Task<T?> GetAsync<T>(
        string key,
        JsonSerializerOptions? serializerOptions,
        CancellationToken cancellationToken = default)
        where T : class
    {
        byte[]? cachedValue = await _distributedCache.GetAsync(
            key,
            cancellationToken);

        return cachedValue is null
            ? null
            : JsonSerializer.Deserialize<T>(
                cachedValue,
                serializerOptions);
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan slidingExpiration,
        JsonSerializerOptions? serializerOptions,
        CancellationToken cancellationToken = default)
        where T : class
    {
        byte[] cachedValue = Serialize(value, serializerOptions);

        var cacheEntryOptions = new DistributedCacheEntryOptions()
            .SetSlidingExpiration(slidingExpiration);

        await _distributedCache.SetAsync(key, cachedValue, cacheEntryOptions, cancellationToken);
    }
    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan slidingExpiration,
        CancellationToken cancellationToken = default)
        where T : class
    {
        byte[] cachedValue = Serialize(value, null);

        var cacheEntryOptions = new DistributedCacheEntryOptions()
            .SetSlidingExpiration(slidingExpiration);
        await _distributedCache.SetAsync(key, cachedValue, cacheEntryOptions, cancellationToken);
    }
    public async Task SetAsync<T>(
        string key,
        T value,
        DistributedCacheEntryOptions cacheOptions,
        CancellationToken cancellationToken = default)
        where T : class
        => await SetAsync(
            key,
            value,
            cacheOptions,
            null,
            cancellationToken);

    public async Task SetAsync<T>(
        string key,
        T value,
        DistributedCacheEntryOptions cacheOptions,
        JsonSerializerOptions? serializerOptions,
        CancellationToken cancellationToken = default)
        where T : class
    {
        byte[] cachedValue = Serialize(value, serializerOptions);
        await _distributedCache.SetAsync(key, cachedValue, cacheOptions, cancellationToken);
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
        => await _distributedCache.RemoveAsync(key, cancellationToken);

    public async Task<IEnumerable<T?>> GetBulkAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
        => await GetBulkAsync<T>(
            keys,
            null,
            cancellationToken);

    public async Task<IEnumerable<T?>> GetBulkAsync<T>(
        IEnumerable<string> keys,
        JsonSerializerOptions? serializerOptions,
        CancellationToken cancellationToken = default)
    {
        var tasks = keys.Select(key => _distributedCache.GetAsync(key, cancellationToken)).ToList();
        var cachedValues = await Task.WhenAll(tasks);
        return cachedValues
            .Select(bytes => JsonSerializer.Deserialize<T>(bytes, serializerOptions));
    }

    private static byte[] Serialize<T>(T value, JsonSerializerOptions? options)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        JsonSerializer.Serialize(writer, value, options);
        return buffer.WrittenSpan.ToArray();
    }
}