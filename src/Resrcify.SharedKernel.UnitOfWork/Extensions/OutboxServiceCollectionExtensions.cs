using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// Registers outbox processing for <typeparamref name="TDbContext"/> with the serializer already registered (or
    /// <c>SystemTextJsonOutboxSerializer</c> when none is), tuned by <paramref name="configure"/>: what
    /// <see cref="AddOutboxProcessing{TDbContext}(IServiceCollection, IOutboxSerializer?, Action{OutboxOptions}?)"/>
    /// registers without a serializer. On PostgreSQL, <c>AddPostgresDbContext(..., db =&gt; db.WithOutbox())</c> registers
    /// the serializer the interceptor writes with, so this reads with the same one:
    /// <code>
    /// services.AddPostgresDbContext&lt;AppDbContext&gt;(configuration, db =&gt; db.WithOutbox());
    /// services.AddOutboxProcessing&lt;AppDbContext&gt;(outbox =&gt; outbox.ProcessIntervalInSeconds = 20);
    /// </code>
    /// </summary>
    public static IServiceCollection AddOutboxProcessing<TDbContext>(
        this IServiceCollection services,
        Action<OutboxOptions> configure)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(configure);
        return services.AddOutboxProcessing<TDbContext>(serializer: null, configure);
    }

    /// <summary>
    /// Registers outbox processing for <typeparamref name="TDbContext"/>: the unit of work, the serializer the job reads
    /// with, and the Quartz processing job.
    /// <para>
    /// The serializer is the registered <see cref="IOutboxSerializer"/>, the one the outbox interceptor writes with:
    /// <list type="bullet">
    /// <item><paramref name="serializer"/> <see langword="null"/> (or left out) uses the registered one, or registers
    /// <c>SystemTextJsonOutboxSerializer</c> when none is (e.g. <c>AddPostgresDbContext(... WithOutbox())</c> registered
    /// one already);</item>
    /// <item>a <paramref name="serializer"/> is registered when none is; when one is, it must be that instance or of its
    /// type (the built-in serializer is stateless, so two of them read and write alike), or this throws an
    /// <see cref="InvalidOperationException"/>: the jobs and the interceptor would read and write with different ones.
    /// It is never silently ignored.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Without the Postgres package, the write side stays explicit: add <c>InsertOutboxMessagesInterceptor</c> to the
    /// DbContext yourself with the <em>same</em> serializer instance, or let the container build it
    /// (<c>AddOutboxInterceptor</c>, which uses the registered serializer):
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
    /// <exception cref="InvalidOperationException">
    /// <paramref name="serializer"/> is of another type than the <see cref="IOutboxSerializer"/> already registered.
    /// </exception>
    public static IServiceCollection AddOutboxProcessing<TDbContext>(
        this IServiceCollection services,
        IOutboxSerializer? serializer = null,
        Action<OutboxOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new OutboxOptions();
        configure?.Invoke(options);
        // A batch of 0 would never come back short, so a run would keep reading until its time is up.
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ProcessIntervalInSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxRetryCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.DelayInSecondsBeforeStart);
        ArgumentOutOfRangeException.ThrowIfNegative(options.ProcessedRetentionInDays);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BacklogCheckIntervalInSeconds, 1);
        OutboxRetryBackoff.Validate(
            options.RetryDelay,
            options.MaxRetryDelay,
            nameof(OutboxOptions.RetryDelay),
            nameof(OutboxOptions.MaxRetryDelay));
        var time = options.TimeProvider ?? RegisteredTimeProvider(services) ?? TimeProvider.System;

        UseOneSerializer<TDbContext>(services, serializer);
        // The clock outbox processing, cleanup and the lanes read; replace it (e.g. with FakeTimeProvider) in tests.
        services.TryAddSingleton(time);
        services.TryAddScoped<IUnitOfWork, UnitOfWork<TDbContext>>();
        services.AddOutboxMessageContext();

        // AddQuartz composes: every call after the first adds to the same default scheduler
        // (its registrations are TryAdd), so this is safe alongside the application's own
        // AddQuartz call, in either order.
        services.AddQuartz(quartz => ProcessOutboxMessagesJobSetup.AddProcessOutboxMessagesJob<TDbContext>(
            quartz,
            options.BatchSize,
            options.ProcessIntervalInSeconds,
            options.DelayInSecondsBeforeStart,
            options.MaxRetryCount,
            options.ProcessedRetentionInDays,
            options.Claim,
            time,
            options.RetryDelay,
            options.MaxRetryDelay));

        // The backlog monitor behind the outbox.messages.* gauges and the outbox health check (AddOutbox<TDbContext>).
        // The job registration above registered it with the default interval; these options replace that.
        services.AddOutboxBacklogMonitor(
            new OutboxBacklogSettings<TDbContext>(
                options.MaxRetryCount,
                TimeSpan.FromSeconds(options.BacklogCheckIntervalInSeconds)),
            replace: true);

        // Outbox lanes (e.g. scatter-gather) with defaults, unless AddOutboxLanes tuned them already; either way they
        // claim with the outbox's claim when they have none of their own (several instances run the lanes too).
        if (!services.HasOutboxLanes<TDbContext>())
            services.AddOutboxLanes<TDbContext>();
        else
            services.ClaimOutboxLanesWith<TDbContext>(options.Claim);

        return services;
    }

    // The interceptor writes with the registered serializer and every outbox job reads with it, so there is one: a
    // serializer given here that isn't (an equivalent of) the registered one would read messages it didn't write.
    private static void UseOneSerializer<TDbContext>(
        IServiceCollection services,
        IOutboxSerializer? serializer)
        where TDbContext : DbContext
    {
        var registered = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IOutboxSerializer) && !descriptor.IsKeyedService);
        if (registered is null)
        {
            if (serializer is null)
                services.AddSingleton<IOutboxSerializer, SystemTextJsonOutboxSerializer>();
            else
                services.AddSingleton(serializer);
            return;
        }

        if (serializer is null || IsSameSerializer(registered, serializer))
            return;

        var registeredName = (registered.ImplementationInstance?.GetType() ?? registered.ImplementationType)?.Name
            ?? "a serializer built by a factory";
        var context = typeof(TDbContext).Name;
        throw new InvalidOperationException(
            $"AddOutboxProcessing<{context}> was given {serializer.GetType().Name}, but {registeredName} is registered "
            + "already as the IOutboxSerializer (by AddPostgresDbContext(... WithOutbox()), AddOutboxInterceptor or an "
            + "earlier registration): the outbox interceptor writes with that one, and the jobs read with it. Pass no "
            + $"serializer (AddOutboxProcessing<{context}>(outbox => ...)) to use the registered one, or register the "
            + "same serializer everywhere (WithOutbox(outbox => outbox.Serializer = ...)).");
    }

    private static bool IsSameSerializer(
        ServiceDescriptor registered,
        IOutboxSerializer serializer)
    {
        if (ReferenceEquals(registered.ImplementationInstance, serializer))
            return true;

        var registeredType = registered.ImplementationInstance?.GetType() ?? registered.ImplementationType;
        return registeredType == serializer.GetType();
    }

    private static TimeProvider? RegisteredTimeProvider(IServiceCollection services)
        => services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(TimeProvider))?.ImplementationInstance
            as TimeProvider;
}
