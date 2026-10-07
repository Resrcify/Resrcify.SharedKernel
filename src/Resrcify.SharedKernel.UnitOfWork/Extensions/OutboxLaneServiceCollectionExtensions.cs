using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>Registers the outbox lanes for a DbContext.</summary>
public static class OutboxLaneServiceCollectionExtensions
{
    /// <summary>
    /// Runs the outbox lanes of <typeparamref name="TDbContext"/>: every event with a scatter-gather handler
    /// (its <c>scatter-gather</c> lane), plus any registered <c>IOutboxLaneEvent</c>. With none, this
    /// does nothing. <c>AddOutboxProcessing</c> calls it with defaults; call it yourself
    /// to tune the lanes, or when the outbox job is registered directly on the Quartz builder. Without a
    /// <see cref="OutboxLaneOptions.Claim"/> of their own, the lanes claim with the outbox job's (<c>OutboxOptions.Claim</c>),
    /// whichever is registered first.
    /// </summary>
    public static IServiceCollection AddOutboxLanes<TDbContext>(
        this IServiceCollection services,
        Action<OutboxLaneOptions>? configure = null)
        where TDbContext : DbContext
    {
        var options = new OutboxLaneOptions();
        configure?.Invoke(options);
        options.Claim ??= services.OutboxJobClaim<TDbContext>();

        services.RemoveAll<OutboxLaneSettings<TDbContext>>();
        services.AddSingleton(new OutboxLaneSettings<TDbContext>(options));
        services.TryAddSingleton<OutboxLaneRegistry>();
        services.TryAddSingleton<OutboxWakeUp<TDbContext>>();
        services.AddOutboxMessageContext();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxLanesHost<TDbContext>>());
        return services;
    }

    /// <summary>Gives the lanes <paramref name="claim"/> when they have none of their own.</summary>
    internal static void ClaimOutboxLanesWith<TDbContext>(this IServiceCollection services, IOutboxLaneClaim? claim)
        where TDbContext : DbContext
    {
        var settings = services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(OutboxLaneSettings<TDbContext>))?
            .ImplementationInstance as OutboxLaneSettings<TDbContext>;
        settings?.Options.Claim ??= claim;
    }

    private static IOutboxLaneClaim? OutboxJobClaim<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
        => (services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(OutboxJobClaim<TDbContext>))?
            .ImplementationInstance as OutboxJobClaim<TDbContext>)?.Claim;

    internal static bool HasOutboxLanes<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
        => services.Any(descriptor => descriptor.ServiceType == typeof(OutboxLaneSettings<TDbContext>));
}
