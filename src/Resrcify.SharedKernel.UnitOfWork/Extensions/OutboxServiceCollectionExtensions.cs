using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quartz;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Primitives;

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
    /// host Quartz with <c>AddQuartz()</c> / <c>AddQuartzHostedService()</c>.
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

        services.TryAddSingleton(serializer);
        services.TryAddScoped<IUnitOfWork, UnitOfWork<TDbContext>>();

        services.AddSingleton<IConfigureOptions<QuartzOptions>>(
            new ProcessOutboxMessagesJobSetup<TDbContext>(
                options.BatchSize,
                options.ProcessIntervalInSeconds,
                options.DelayInSecondsBeforeStart,
                options.MaxRetryCount));

        return services;
    }
}
