using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Distributed;
using Resrcify.SharedKernel.Caching.Primitives;

namespace Resrcify.SharedKernel.PerformanceTests.Caching;

[MemoryDiagnoser]
public class CachingBenchmarks
{
    private DistributedCachingService _cachingService = default!;
    private static readonly TestPayload Payload = new(42, "bench");
    private static readonly string[] BulkKeys = ["payload", "payload", "payload"];

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var distributedCache = new InMemoryDistributedCache();
        _cachingService = new DistributedCachingService(distributedCache);

        await _cachingService.SetAsync(
            "payload",
            Payload,
            slidingExpiration: TimeSpan.FromMinutes(5),
            cancellationToken: CancellationToken.None);
    }

    [Benchmark(Baseline = true)]
    public Task SetAsync()
        => _cachingService.SetAsync(
            "payload",
            Payload,
            slidingExpiration: TimeSpan.FromMinutes(5),
            cancellationToken: CancellationToken.None);

    [Benchmark]
    public Task<TestPayload?> GetAsync()
        => _cachingService.GetAsync<TestPayload>(
            "payload",
            cancellationToken: CancellationToken.None);

    [Benchmark]
    public Task<IEnumerable<TestPayload?>> GetBulkAsync()
        => _cachingService.GetBulkAsync<TestPayload>(
            BulkKeys,
            cancellationToken: CancellationToken.None);

    public static void SelfTest()
    {
        var instance = new CachingBenchmarks();
        instance.GlobalSetup().GetAwaiter().GetResult();
        instance.SetAsync().GetAwaiter().GetResult();
        _ = instance.GetAsync().GetAwaiter().GetResult();
        _ = instance.GetBulkAsync().GetAwaiter().GetResult();
    }

    public sealed record TestPayload(int Number, string Text);

    private sealed class InMemoryDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _store =
            new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);

        public byte[]? Get(string key)
            => _store.TryGetValue(key, out var value)
                ? value
                : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => _store[key] = value;

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
            => Task.CompletedTask;

        public void Remove(string key)
            => _store.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
