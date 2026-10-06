using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Abstractions.Caching;

/// <summary>
/// A JSON cache. Every entry expires, and how is chosen by the method's name, so it can't be mistaken:
/// <list type="bullet">
/// <item><c>SetAsync(key, value, absoluteExpiration)</c>: kept until a set time.</item>
/// <item><c>SetForAsync(key, value, expiresIn)</c>: kept for a set time from now; reads don't extend it.</item>
/// <item><c>SetSlidingAsync(key, value, slidingExpiration)</c>: kept while it is read at least that often.</item>
/// </list>
/// </summary>
public interface ICachingService
{
    Task<T?> GetAsync<T>(
        string key,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
        where T : class
        => GetAsync<T>(
            key,
            serializerOptions: null,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Caches <paramref name="value"/> with the given expirations: at least one of them (an entry that never expires
    /// is refused). The method an implementation provides; callers usually pick one of the named overloads.
    /// <paramref name="absoluteExpirationRelativeToNow"/> is measured on the cache's own clock, so a duration needs no
    /// clock reading here.
    /// </summary>
    /// <exception cref="ArgumentException">No expiration is given.</exception>
    Task SetAsync<T>(
        string key,
        T value,
        DateTimeOffset? absoluteExpiration,
        TimeSpan? absoluteExpirationRelativeToNow,
        TimeSpan? slidingExpiration,
        JsonSerializerOptions? serializerOptions,
        CancellationToken cancellationToken)
        where T : class;

    /// <summary>Caches <paramref name="value"/> until <paramref name="absoluteExpiration"/>.</summary>
    Task SetAsync<T>(
        string key,
        T value,
        DateTimeOffset absoluteExpiration,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        where T : class
        => SetAsync(
            key,
            value,
            absoluteExpiration: absoluteExpiration,
            absoluteExpirationRelativeToNow: null,
            slidingExpiration: null,
            serializerOptions: serializerOptions,
            cancellationToken: cancellationToken);

    /// <inheritdoc cref="SetAsync{T}(string, T, DateTimeOffset, JsonSerializerOptions?, CancellationToken)"/>
    Task SetAsync<T>(
        string key,
        T value,
        DateTimeOffset absoluteExpiration,
        CancellationToken cancellationToken)
        where T : class
        => SetAsync(
            key,
            value,
            absoluteExpiration,
            serializerOptions: null,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Caches <paramref name="value"/> for <paramref name="expiresIn"/> from now; reads don't extend it. Use it for an
    /// entry that must expire on time however often it is read (a one-time code, a cached query result).
    /// </summary>
    Task SetForAsync<T>(
        string key,
        T value,
        TimeSpan expiresIn,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresIn, TimeSpan.Zero);
        return SetAsync(
            key,
            value,
            absoluteExpiration: null,
            absoluteExpirationRelativeToNow: expiresIn,
            slidingExpiration: null,
            serializerOptions: serializerOptions,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc cref="SetForAsync{T}(string, T, TimeSpan, JsonSerializerOptions?, CancellationToken)"/>
    Task SetForAsync<T>(
        string key,
        T value,
        TimeSpan expiresIn,
        CancellationToken cancellationToken)
        where T : class
        => SetForAsync(
            key,
            value,
            expiresIn,
            serializerOptions: null,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Caches <paramref name="value"/> with a sliding expiration: every read keeps it for
    /// <paramref name="slidingExpiration"/> again, and it goes once it isn't read for that long.
    /// </summary>
    Task SetSlidingAsync<T>(
        string key,
        T value,
        TimeSpan slidingExpiration,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        where T : class
        => SetAsync(
            key,
            value,
            absoluteExpiration: null,
            absoluteExpirationRelativeToNow: null,
            slidingExpiration: slidingExpiration,
            serializerOptions: serializerOptions,
            cancellationToken: cancellationToken);

    /// <inheritdoc cref="SetSlidingAsync{T}(string, T, TimeSpan, JsonSerializerOptions?, CancellationToken)"/>
    Task SetSlidingAsync<T>(
        string key,
        T value,
        TimeSpan slidingExpiration,
        CancellationToken cancellationToken)
        where T : class
        => SetSlidingAsync(
            key,
            value,
            slidingExpiration,
            serializerOptions: null,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Removes the entry cached under <paramref name="key"/>, or releases the claim on it
    /// (<see cref="TryClaimForAsync"/>).
    /// </summary>
    Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims <paramref name="key"/> for <paramref name="expiresIn"/> from now, if nobody holds it: <see langword="true"/>
    /// when this call claimed it, <see langword="false"/> when a claim on it is already held (it hasn't expired, nor been
    /// released with <see cref="RemoveAsync"/>). For doing something once: the caller that claims a key does it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A claim is a marker, not a cached value. Keep claim keys apart from the keys values are cached under: a claim key is
    /// only used with <see cref="TryClaimForAsync"/> and <see cref="RemoveAsync"/>, never read with <c>GetAsync</c> or
    /// overwritten with <c>SetAsync</c> (an implementation may store a claim in a format they don't read).
    /// </para>
    /// <para>
    /// Two callers claiming the same key at once get one <see langword="true"/> between them only as far as the
    /// implementation makes the claim atomic: across processes, only where the store can set a key that is absent in one
    /// step (Redis <c>SET key value PX ms NX</c>). <c>DistributedCachingService</c> is atomic within one process only
    /// (<c>IDistributedCache</c> has no such step).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expiresIn"/> isn't positive.</exception>
    Task<bool> TryClaimForAsync(
        string key,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<T?>> GetBulkAsync<T>(
        IEnumerable<string> keys,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<T?>> GetBulkAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
        => GetBulkAsync<T>(
            keys,
            serializerOptions: null,
            cancellationToken: cancellationToken);
}
