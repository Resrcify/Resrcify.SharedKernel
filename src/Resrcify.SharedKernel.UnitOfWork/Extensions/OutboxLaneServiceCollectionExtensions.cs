using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
    /// to tune the lanes, or when the outbox job is registered directly on the Quartz builder.
    /// </summary>
    public static IServiceCollection AddOutboxLanes<TDbContext>(
        this IServiceCollection services,
        Action<OutboxLaneOptions>? configure = null)
        where TDbContext : DbContext
    {
        var options = new OutboxLaneOptions();
        configure?.Invoke(options);

        services.RemoveAll<OutboxLaneSettings<TDbContext>>();
        services.AddSingleton(new OutboxLaneSettings<TDbContext>(options));
        services.TryAddSingleton<OutboxLaneRegistry>();
        services.TryAddSingleton<OutboxWakeUp<TDbContext>>();
        services.AddOutboxMessageContext();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxLanesHost<TDbContext>>());
        return services;
    }

    internal static bool HasOutboxLanes<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
        => services.Any(descriptor => descriptor.ServiceType == typeof(OutboxLaneSettings<TDbContext>));
}
