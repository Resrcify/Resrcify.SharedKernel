using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Registers <typeparamref name="TDbContext"/>'s backlog monitor (behind the <c>outbox.messages.*</c> gauges and the
/// <c>AddOutbox&lt;TDbContext&gt;</c> health check) once, whichever outbox registration asks for it first:
/// <c>AddOutboxProcessing</c> or a bare <c>AddProcessOutboxMessagesJob</c>.
/// </summary>
internal static class OutboxBacklogMonitorRegistration
{
    /// <summary>How often the backlog is measured unless <c>OutboxOptions.BacklogCheckIntervalInSeconds</c> says otherwise.</summary>
    public static readonly TimeSpan DefaultInterval = OutboxOptions.DefaultBacklogCheckInterval;

    /// <summary>
    /// Registers the monitor (a hosted service) and the outbox administration, once. <paramref name="settings"/> replace
    /// any registered before when <paramref name="replace"/> is <see langword="true"/> (<c>AddOutboxProcessing</c>, whose
    /// options are explicit), and are only a default otherwise (the bare job, which doesn't know the interval).
    /// </summary>
    public static IServiceCollection AddOutboxBacklogMonitor<TDbContext>(
        this IServiceCollection services,
        OutboxBacklogSettings<TDbContext> settings,
        bool replace)
        where TDbContext : DbContext
    {
        if (replace)
            services.RemoveAll<OutboxBacklogSettings<TDbContext>>();
        services.TryAddSingleton(settings);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddOutboxAdministration<TDbContext>();

        // A second hosted-service registration would start the same monitor twice.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(OutboxBacklogMonitor<TDbContext>)))
            return services;

        services.AddSingleton<OutboxBacklogMonitor<TDbContext>>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<OutboxBacklogMonitor<TDbContext>>());
        return services;
    }
}
