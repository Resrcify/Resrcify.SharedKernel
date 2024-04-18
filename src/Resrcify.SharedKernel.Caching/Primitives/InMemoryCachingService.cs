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


internal class InMemoryCachingService : ICachingService
{
    private readonly IDistributedCache _distributedCache;

    public InMemoryCachingService(IDistributedCache distributedCache)
        => _distributedCache = distributedCache;

    public async Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
        where T : class
    {
        byte[]? cachedValue = await _distributedCache.GetAsync(key, cancellationToken);
        if (cachedValue is null)
            return null;
        return JsonSerializer.Deserialize<T>(cachedValue);
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
        where T : class
    {
        byte[] cachedValue = Serialize(value);
        var options = new DistributedCacheEntryOptions()
            .SetSlidingExpiration(expiration);
        await _distributedCache.SetAsync(key, cachedValue, options, cancellationToken);
    }
    private static byte[] Serialize<T>(T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        JsonSerializer.Serialize(writer, value);
        return buffer.WrittenSpan.ToArray();
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => await _distributedCache.RemoveAsync(key, cancellationToken);

    public async Task<IEnumerable<T?>> GetBulkAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var tasks = keys.Select(key => _distributedCache.GetAsync(key, cancellationToken)).ToList();
        var cachedValues = await Task.WhenAll(tasks);
        return cachedValues
            .Select(bytes => JsonSerializer.Deserialize<T>(bytes));
    }
}