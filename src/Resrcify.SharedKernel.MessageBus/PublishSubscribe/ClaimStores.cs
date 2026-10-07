using System;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Caching;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>Where <c>SkipDuplicateEvents</c> claims the events it handles.</summary>
internal static class ClaimStores
{
    /// <summary>
    /// The registered <see cref="IClaimStore"/>, or the registered <see cref="ICachingService"/> when it is one too (as
    /// <c>DistributedCachingService</c> is), so a service that registered its cache needs nothing more.
    /// </summary>
    public static IClaimStore? Of(IServiceProvider provider)
        => provider.GetService<IClaimStore>() ?? provider.GetService<ICachingService>() as IClaimStore;
}
