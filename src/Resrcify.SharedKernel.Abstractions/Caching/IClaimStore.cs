using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Abstractions.Caching;

/// <summary>
/// Claims a key for a while, if nobody holds it: for doing something once (the message bus' <c>SkipDuplicateEvents</c>
/// claims each event it handles). Not a cache: a claim is a marker, read only through this interface.
/// <c>DistributedCachingService</c> is one, over its <c>IDistributedCache</c>.
/// </summary>
public interface IClaimStore
{
    /// <summary>
    /// Claims <paramref name="key"/> for <paramref name="expiresIn"/> from now, if nobody holds it: <see langword="true"/>
    /// when this call claimed it, <see langword="false"/> when a claim on it is already held (it hasn't expired, nor been
    /// released with <see cref="ReleaseAsync"/>).
    /// </summary>
    /// <remarks>
    /// Two callers claiming the same key at once get one <see langword="true"/> between them only as far as the
    /// implementation makes the claim atomic: across processes, only where the store can set a key that is absent in one
    /// step (Redis <c>SET key value PX ms NX</c>). <c>DistributedCachingService</c> is atomic within one process only
    /// (<c>IDistributedCache</c> has no such step).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="expiresIn"/> isn't positive, or is longer than <see cref="ICachingService.MaxLifetime"/>.
    /// </exception>
    Task<bool> TryClaimForAsync(
        string key,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);

    /// <summary>Releases the claim on <paramref name="key"/>, so the next <see cref="TryClaimForAsync"/> gets it.</summary>
    Task ReleaseAsync(
        string key,
        CancellationToken cancellationToken = default);
}
