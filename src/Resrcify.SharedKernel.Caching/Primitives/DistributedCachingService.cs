using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Text.Json;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.Abstractions.Caching;

namespace Resrcify.SharedKernel.Caching.Primitives;


public sealed class DistributedCachingService
    : ICachingService
{
    private const int DefaultBulkBatchSize = 128;

    private readonly IDistributedCache _distributedCache;

    public DistributedCachingService(IDistributedCache distributedCache)
        => _distributedCache = distributedCache;

    public async Task<T?> GetAsync<T>(
        string key,
        JsonSerializerOptions? serializerOptions = null,
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
        TimeSpan? slidingExpiration = null,
        DateTimeOffset? absoluteExpiration = null,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        byte[] cachedValue = Serialize(value, serializerOptions);
        await _distributedCache.SetAsync(
            key,
            cachedValue,
            ToDistributedCacheEntryOptions(
                slidingExpiration,
                absoluteExpiration),
            cancellationToken);
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
        => await _distributedCache.RemoveAsync(key, cancellationToken);

    public async Task<IEnumerable<T?>> GetBulkAsync<T>(
        IEnumerable<string> keys,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var values = new List<T?>();

        foreach (var keyBatch in keys.Chunk(DefaultBulkBatchSize))
        {
            var tasks = keyBatch.Select(
                key => _distributedCache.GetAsync(
                    key,
                    cancellationToken));

            var cachedValues = await Task.WhenAll(tasks);

            values.AddRange(
                cachedValues.Select(
                    bytes => bytes is null
                        ? default
                        : JsonSerializer.Deserialize<T>(
                            bytes,
                            serializerOptions)));
        }

        return values;
    }

    private static byte[] Serialize<T>(T value, JsonSerializerOptions? options)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        JsonSerializer.Serialize(writer, value, options);
        return buffer.WrittenSpan.ToArray();
    }

    private static DistributedCacheEntryOptions ToDistributedCacheEntryOptions(
        TimeSpan? slidingExpiration,
        DateTimeOffset? absoluteExpiration)
    {
        var distributedCacheOptions = new DistributedCacheEntryOptions();

        if (absoluteExpiration.HasValue)
            distributedCacheOptions.AbsoluteExpiration = absoluteExpiration;

        if (slidingExpiration.HasValue)
            distributedCacheOptions.SlidingExpiration = slidingExpiration;

        return distributedCacheOptions;
    }
}