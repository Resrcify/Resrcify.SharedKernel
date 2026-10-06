using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.Abstractions.Caching;

namespace Resrcify.SharedKernel.Caching.Primitives;


public sealed class DistributedCachingService
    : ICachingService
{
    private const int DefaultBulkBatchSize = 128;

    /// <summary>Claims are made one at a time per stripe of keys, so two claims of one key in this process can't both win.</summary>
    private const int ClaimStripeCount = 64;

    /// <summary>What a claim stores: valid JSON, though a claim key isn't meant to be read.</summary>
    private static readonly byte[] ClaimMarker = "\"claimed\""u8.ToArray();

    // Shared by every instance: two services over one cache in this process must not both claim a key either.
    private static readonly SemaphoreSlim[] ClaimStripes = CreateClaimStripes();

    private readonly IDistributedCache _distributedCache;
    private readonly TimeProvider _time;

    /// <param name="distributedCache">Where entries are kept; it measures their expiry on its own clock.</param>
    /// <param name="timeProvider">The clock a lifetime is checked against (the system clock by default).</param>
    public DistributedCachingService(IDistributedCache distributedCache, TimeProvider? timeProvider = null)
    {
        _distributedCache = distributedCache;
        _time = timeProvider ?? TimeProvider.System;
    }

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
        DateTimeOffset? absoluteExpiration,
        TimeSpan? absoluteExpirationRelativeToNow,
        TimeSpan? slidingExpiration,
        JsonSerializerOptions? serializerOptions,
        CancellationToken cancellationToken)
        where T : class
    {
        // Every entry expires: one that never does stays until the cache runs out of memory.
        if (absoluteExpiration is null && absoluteExpirationRelativeToNow is null && slidingExpiration is null)
            throw new ArgumentException(
                $"'{key}' has no expiration: cache it with SetForAsync, SetSlidingAsync or an absolute expiration.",
                nameof(absoluteExpiration));
        if (absoluteExpirationRelativeToNow > DateTimeOffset.MaxValue - _time.GetUtcNow())
            throw new ArgumentOutOfRangeException(
                nameof(absoluteExpirationRelativeToNow),
                absoluteExpirationRelativeToNow,
                "The entry would expire after the last date there is: give it a real lifetime (for example days), not TimeSpan.MaxValue.");

        byte[] cachedValue = Serialize(value, serializerOptions);
        await _distributedCache.SetAsync(
            key,
            cachedValue,
            ToDistributedCacheEntryOptions(
                slidingExpiration,
                absoluteExpiration,
                absoluteExpirationRelativeToNow),
            cancellationToken);
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
        => await _distributedCache.RemoveAsync(key, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>IDistributedCache</c> can't set a key only if it is absent, so this reads and then sets, one claim of a key at a
    /// time in this process: atomic within the process, not across processes sharing the cache (two instances can both
    /// claim a key). Where that matters, use an <see cref="ICachingService"/> over a store that claims in one step (Redis
    /// <c>SET NX</c>).
    /// </remarks>
    public async Task<bool> TryClaimForAsync(
        string key,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresIn, TimeSpan.Zero);
        if (expiresIn > DateTimeOffset.MaxValue - _time.GetUtcNow())
            throw new ArgumentOutOfRangeException(
                nameof(expiresIn),
                expiresIn,
                "The claim would expire after the last date there is: give it a real lifetime.");

        var stripe = ClaimStripes[(uint)StringComparer.Ordinal.GetHashCode(key) % ClaimStripeCount];
        await stripe.WaitAsync(cancellationToken);
        try
        {
            if (await _distributedCache.GetAsync(key, cancellationToken) is not null)
                return false;

            await _distributedCache.SetAsync(
                key,
                ClaimMarker,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiresIn },
                cancellationToken);
            return true;
        }
        finally
        {
            stripe.Release();
        }
    }

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

    // Straight to the array (no buffer copied into it), and with the caller's options for the writer too
    // (indentation, escaping): a writer made here would ignore them.
    private static byte[] Serialize<T>(T value, JsonSerializerOptions? options)
        => JsonSerializer.SerializeToUtf8Bytes(value, options);

    private static SemaphoreSlim[] CreateClaimStripes()
    {
        var stripes = new SemaphoreSlim[ClaimStripeCount];
        for (var i = 0; i < stripes.Length; i++)
            stripes[i] = new SemaphoreSlim(1, 1);
        return stripes;
    }

    private static DistributedCacheEntryOptions ToDistributedCacheEntryOptions(
        TimeSpan? slidingExpiration,
        DateTimeOffset? absoluteExpiration,
        TimeSpan? absoluteExpirationRelativeToNow)
    {
        var distributedCacheOptions = new DistributedCacheEntryOptions();

        if (absoluteExpiration.HasValue)
            distributedCacheOptions.AbsoluteExpiration = absoluteExpiration;

        if (absoluteExpirationRelativeToNow.HasValue)
            distributedCacheOptions.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow;

        if (slidingExpiration.HasValue)
            distributedCacheOptions.SlidingExpiration = slidingExpiration;

        return distributedCacheOptions;
    }
}