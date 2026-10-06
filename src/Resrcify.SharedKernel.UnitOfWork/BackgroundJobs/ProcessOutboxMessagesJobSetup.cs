using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Registers <see cref="ProcessOutboxMessagesJob{TDbContext}"/> on a Quartz scheduler.
/// </summary>
/// <remarks>
/// Quartz 4 removed the <c>QuartzOptions.AddJob</c> / <c>AddTrigger</c> extensions this used to call from an
/// <c>IConfigureOptions&lt;QuartzOptions&gt;</c>, so the registration now hangs off the builder that
/// <c>AddQuartz</c> hands out:
/// <code>
/// services.AddQuartz(quartz => quartz.AddProcessOutboxMessagesJob&lt;AppDbContext&gt;());
/// </code>
/// Both the job and the trigger are built when the scheduler is, not when this is called, so
/// <paramref name="delayInSecondsBeforeStart"/> still counts from scheduler start-up, as it did before.
/// </remarks>
public static class ProcessOutboxMessagesJobSetup
{
    /// <param name="quartz">The Quartz builder.</param>
    /// <param name="processBatchSize">Messages read per run.</param>
    /// <param name="processIntervalInSeconds">
    /// Seconds between runs. A run drains a backlog (it reads batch after batch while they come back full) for up to
    /// 80% of this.
    /// </param>
    /// <param name="delayInSecondsBeforeStart">Seconds after start-up before the first run.</param>
    /// <param name="processMaxRetryCount">Attempts a message gets before it is poison.</param>
    /// <param name="processedRetentionInDays">Days processed messages are kept (an hourly cleanup); 0 turns it off.</param>
    /// <param name="claim">
    /// Claims each message for its processing transaction, so several service instances don't each publish it.
    /// <see langword="null"/> is fine for one instance; use <c>PostgresOutboxLaneClaim.Instance</c> on PostgreSQL.
    /// </param>
    /// <param name="timeProvider">
    /// The clock the first start is computed from; <see cref="TimeProvider.System"/> when <see langword="null"/>.
    /// This only sets when the triggers first fire: Quartz 4 runs the scheduler itself on the
    /// <see cref="TimeProvider"/> registered in DI (or one given with <c>UseTimeProvider</c>), so register a fake
    /// one there to drive the schedule in tests.
    /// </param>
    public static IQuartzBuilder AddProcessOutboxMessagesJob<TDbContext>(
        this IQuartzBuilder quartz,
        int processBatchSize = ProcessOutboxMessagesJob<DbContext>.DefaultProcessBatchSize,
        int processIntervalInSeconds = 60,
        int delayInSecondsBeforeStart = 60,
        int processMaxRetryCount = ProcessOutboxMessagesJob<DbContext>.DefaultProcessMaxRetryCount,
        int processedRetentionInDays = CleanupOutboxMessagesJob<DbContext>.DefaultRetentionInDays,
        IOutboxLaneClaim? claim = null,
        TimeProvider? timeProvider = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(quartz);
        var time = timeProvider ?? TimeProvider.System;
        quartz.Services.AddOutboxMessageContext();
        quartz.Services.TryAddSingleton<OutboxWakeUp<TDbContext>>();
        quartz.Services.RemoveAll<OutboxJobClaim<TDbContext>>();
        quartz.Services.AddSingleton(new OutboxJobClaim<TDbContext>(claim));
        if (processedRetentionInDays > 0)
            quartz.AddCleanupOutboxMessagesJob<TDbContext>(processedRetentionInDays, time);

        var jobKey = OutboxJobs.Process<TDbContext>();

        return quartz
            .AddJob<ProcessOutboxMessagesJob<TDbContext>>(job => job
                .WithIdentity(jobKey)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessBatchSizeKey,
                    processBatchSize)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessMaxRetryCountKey,
                    processMaxRetryCount)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessIntervalInSecondsKey,
                    processIntervalInSeconds))
            .AddTrigger<ProcessOutboxMessagesJob<TDbContext>>(trigger => trigger
                .ForJob(jobKey)
                .StartAt(time.GetUtcNow().AddSeconds(delayInSecondsBeforeStart))
                .WithSimpleSchedule(schedule => schedule
                    .WithInterval(TimeSpan.FromSeconds(processIntervalInSeconds))
                    .RepeatForever()));
    }

    /// <summary>
    /// Deletes processed outbox messages older than <paramref name="processedRetentionInDays"/> every hour (first run
    /// five minutes after start-up). <see cref="AddProcessOutboxMessagesJob{TDbContext}"/> adds it, with 7 days.
    /// </summary>
    /// <param name="timeProvider">
    /// The clock the first start is computed from; <see cref="TimeProvider.System"/> when <see langword="null"/>.
    /// The scheduler itself runs on the <see cref="TimeProvider"/> registered in DI (Quartz 4).
    /// </param>
    public static IQuartzBuilder AddCleanupOutboxMessagesJob<TDbContext>(
        this IQuartzBuilder quartz,
        int processedRetentionInDays = CleanupOutboxMessagesJob<DbContext>.DefaultRetentionInDays,
        TimeProvider? timeProvider = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(quartz);
        ArgumentOutOfRangeException.ThrowIfLessThan(processedRetentionInDays, 1);

        var time = timeProvider ?? TimeProvider.System;
        var jobKey = OutboxJobs.Cleanup<TDbContext>();
        return quartz
            .AddJob<CleanupOutboxMessagesJob<TDbContext>>(job => job
                .WithIdentity(jobKey)
                .UsingJobData(CleanupOutboxMessagesJob<TDbContext>.RetentionInDaysKey, processedRetentionInDays))
            .AddTrigger<CleanupOutboxMessagesJob<TDbContext>>(trigger => trigger
                .ForJob(jobKey)
                .StartAt(time.GetUtcNow().AddMinutes(5))
                .WithSimpleSchedule(schedule => schedule
                    .WithInterval(TimeSpan.FromHours(1))
                    .RepeatForever()));
    }
}
