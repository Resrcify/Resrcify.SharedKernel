using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Caching.Extensions;
using Resrcify.SharedKernel.Caching.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Caching.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class CachingServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddDistributedCaching_ShouldRegisterOneServiceAsTheCacheAndTheClaimStore_OverTheMemoryCache()
    {
        var services = new ServiceCollection();

        services.AddDistributedCaching();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var caching = provider.GetRequiredService<ICachingService>();
        caching.ShouldBeOfType<DistributedCachingService>();
        provider.GetRequiredService<IClaimStore>().ShouldBeSameAs(caching);
        provider.GetRequiredService<IDistributedCache>().ShouldBeOfType<MemoryDistributedCache>();
        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);

        await caching.SetForAsync("key", "value", TimeSpan.FromMinutes(1), cancellationToken: CancellationToken.None);
        (await caching.GetAsync<string>("key", CancellationToken.None)).ShouldBe("value");
    }

    [Fact]
    public async Task AddDistributedCaching_ShouldTuneTheMemoryCache_WhenAskedTo()
    {
        var services = new ServiceCollection();

        services.AddDistributedCaching(cache => cache.SizeLimit = 1024);

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<MemoryDistributedCacheOptions>>().Value.SizeLimit.ShouldBe(1024);
    }

    [Fact]
    public async Task AddDistributedCaching_ShouldKeepTheDistributedCache_RegisteredBefore()
    {
        var redis = Substitute.For<IDistributedCache>();
        var services = new ServiceCollection();
        services.AddSingleton(redis);

        services.AddDistributedCaching();

        services.Count(descriptor => descriptor.ServiceType == typeof(IDistributedCache)).ShouldBe(1);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IDistributedCache>().ShouldBeSameAs(redis);
    }

    [Fact]
    public void AddDistributedCaching_ShouldRegisterEachServiceOnce_WhenCalledTwice()
    {
        var services = new ServiceCollection();

        services.AddDistributedCaching();
        services.AddDistributedCaching();

        services.Count(descriptor => descriptor.ServiceType == typeof(ICachingService)).ShouldBe(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IClaimStore)).ShouldBe(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IDistributedCache)).ShouldBe(1);
    }
}
