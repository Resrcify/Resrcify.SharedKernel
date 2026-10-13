using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Quartz;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Primitives;

using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>
/// Wiring for the read side of the outbox: the unit of work, the serializer the
/// processing job uses, and the Quartz job itself.
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers outbox processing for <typeparamref name="TDbContext"/>: the unit
    /// of work, the <paramref name="serializer"/> the job reads with, and the Quartz
    /// processing job.
    /// <para>
    /// The write side stays explicit — add <c>InsertOutboxMessagesInterceptor</c> to
    /// the DbContext yourself, passing the <em>same</em> <paramref name="serializer"/>
    /// instance so the write and read sides cannot drift apart:
    /// </para>
    /// <code>
    /// var serializer = new SystemTextJsonOutboxSerializer();
    /// services.AddDbContext&lt;AppDbContext&gt;(o => o
    ///     .UseNpgsql(connectionString)
    ///     .AddInterceptors(new InsertOutboxMessagesInterceptor(serializer)));
    /// services.AddOutboxProcessing&lt;AppDbContext&gt;(serializer);
    /// </code>
    /// Register the EF model with
    /// <see cref="OutboxModelBuilderExtensions.ApplyOutboxMessageConfiguration"/> and
    /// start the scheduler with <c>AddQuartzHostedService()</c>; the Quartz registration itself is made here.
    /// </summary>
    public static IServiceCollection AddOutboxProcessing<TDbContext>(
        this IServiceCollection services,
        IOutboxSerializer serializer,
        Action<OutboxOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(serializer);

        var options = new OutboxOptions();
        configure?.Invoke(options);
        // A batch of 0 would never come back short, so a run would keep reading until its time is up.
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ProcessIntervalInSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxRetryCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.DelayInSecondsBeforeStart);
        ArgumentOutOfRangeException.ThrowIfNegative(options.ProcessedRetentionInDays);
        var time = options.TimeProvider ?? RegisteredTimeProvider(services) ?? TimeProvider.System;

        services.TryAddSingleton(serializer);
        // The clock outbox processing, cleanup and the lanes read; replace it (e.g. with FakeTimeProvider) in tests.
        services.TryAddSingleton(time);
        services.TryAddScoped<IUnitOfWork, UnitOfWork<TDbContext>>();
        services.AddOutboxMessageContext();

        // AddQuartz composes: every call after the first adds to the same default scheduler
        // (its registrations are TryAdd), so this is safe alongside the application's own
        // AddQuartz call, in either order.
        services.AddQuartz(quartz => quartz.AddProcessOutboxMessagesJob<TDbContext>(
            options.BatchSize,
            options.ProcessIntervalInSeconds,
            options.DelayInSecondsBeforeStart,
            options.MaxRetryCount,
            options.ProcessedRetentionInDays,
            options.Claim,
            time));

        // The backlog monitor behind the outbox.messages.* gauges and the outbox health check (AddOutbox<TDbContext>).
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BacklogCheckIntervalInSeconds, 1);
        services.AddSingleton(new OutboxBacklogSettings<TDbContext>(
            options.MaxRetryCount,
            TimeSpan.FromSeconds(options.BacklogCheckIntervalInSeconds)));
        services.TryAddSingleton<OutboxBacklogMonitor<TDbContext>>();
        services.TryAddScoped<OutboxAdministration<TDbContext>>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<OutboxBacklogMonitor<TDbContext>>());

        // Outbox lanes (e.g. scatter-gather) with defaults, unless AddOutboxLanes tuned them already; either way they
        // claim with the outbox's claim when they have none of their own (several instances run the lanes too).
        if (!services.HasOutboxLanes<TDbContext>())
            services.AddOutboxLanes<TDbContext>();
        else
            services.ClaimOutboxLanesWith<TDbContext>(options.Claim);

        return services;
    }

    private static TimeProvider? RegisteredTimeProvider(IServiceCollection services)
        => services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(TimeProvider))?.ImplementationInstance
            as TimeProvider;
}
