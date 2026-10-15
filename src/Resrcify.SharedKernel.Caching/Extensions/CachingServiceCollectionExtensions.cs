using System;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Caching.Primitives;

namespace Resrcify.SharedKernel.Caching.Extensions;

/// <summary>Registers the caching service in one call.</summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DistributedCachingService"/> as the <see cref="ICachingService"/> and the
    /// <see cref="IClaimStore"/> (one instance, a singleton), over the registered <see cref="IDistributedCache"/>: the
    /// in-memory one (<c>AddDistributedMemoryCache</c>, tuned by <paramref name="configure"/>) unless one is registered
    /// already (e.g. Redis, registered before this). An <see cref="ICachingService"/> or <see cref="IClaimStore"/>
    /// registered before is kept. The clock is the container's <see cref="TimeProvider"/> (the system one unless one is
    /// registered, before or after).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Tunes the in-memory cache (e.g. <c>options.SizeLimit</c>); unused when another cache is registered.</param>
    /// <example>
    /// <code>
    /// services.AddDistributedCaching();
    /// services.AddDistributedCaching(cache =&gt; cache.SizeLimit = 1024 * 1024 * 1024);
    /// </code>
    /// </example>
    public static IServiceCollection AddDistributedCaching(
        this IServiceCollection services,
        Action<MemoryDistributedCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd inside: an IDistributedCache registered before (Redis) is kept.
        if (configure is null)
            services.AddDistributedMemoryCache();
        else
            services.AddDistributedMemoryCache(configure);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DistributedCachingService>();
        services.TryAddSingleton<ICachingService>(provider => provider.GetRequiredService<DistributedCachingService>());
        services.TryAddSingleton<IClaimStore>(provider => provider.GetRequiredService<DistributedCachingService>());
        return services;
    }
}
