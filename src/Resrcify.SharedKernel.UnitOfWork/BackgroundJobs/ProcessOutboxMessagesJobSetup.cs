using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.UnitOfWork.Primitives;

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
/// <para>
/// <see cref="AddProcessOutboxMessagesJob{TDbContext}"/> also registers the backlog monitor (a hosted service measuring
/// the outbox every 30 s) that the <c>outbox.messages.*</c> gauges and the <c>AddOutbox&lt;TDbContext&gt;</c> health
/// check read, as <c>AddOutboxProcessing</c> does, so the health check works with either registration.
/// </para>
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
    /// <param name="retries">
    /// The attempts a message gets before it is poison, when a try fails for a reason another try won't change, and the
    /// waits between them; <see langword="null"/>: 3 attempts, 5 s after the first failure, then twice as long each time,
    /// up to 5 min.
    /// </param>
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
    /// <param name="transientRetries">
    /// The attempts and waits a message gets when its tries fail for a transient reason (the container's
    /// <c>ITransientFailureClassifier</c>: a <c>TransientFailureException</c>, a cancellation or timeout, a transient
    /// database failure, an HTTP request that got no answer or a 408, 429 or 5xx, or what a registered
    /// <c>ITransientFailureRule</c> calls transient). Unset (<see langword="null"/>): <paramref name="retries"/> with 12
    /// attempts (about 30 minutes with the default delays), or its own when that is higher. Set, at least as many
    /// attempts as <paramref name="retries"/>; as many gives every failure the same tries (as before 4.3.0).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="transientRetries"/> is set with fewer attempts than <paramref name="retries"/>.
    /// </exception>
    public static IQuartzBuilder AddProcessOutboxMessagesJob<TDbContext>(
        this IQuartzBuilder quartz,
        int processBatchSize = ProcessOutboxMessagesJob<DbContext>.DefaultProcessBatchSize,
        int processIntervalInSeconds = 60,
        int delayInSecondsBeforeStart = 60,
        RetrySchedule? retries = null,
        int processedRetentionInDays = CleanupOutboxMessagesJob<DbContext>.DefaultRetentionInDays,
        IOutboxLaneClaim? claim = null,
        TimeProvider? timeProvider = null,
        RetrySchedule? transientRetries = null)
        where TDbContext : DbContext
    {
        var limits = OutboxRetryLimits.Create(
            retries ?? OutboxRetryLimits.DefaultRetries,
            transientRetries,
            nameof(retries),
            nameof(transientRetries));
        return AddProcessOutboxMessagesJob<TDbContext>(
            quartz,
            processBatchSize,
            processIntervalInSeconds,
            delayInSecondsBeforeStart,
            limits,
            processedRetentionInDays,
            claim,
            timeProvider);
    }

    // The registration, with the retry limits its callers made (OutboxRetryLimits.Create): the attempts, delays and
    // jitter of both schedules go in the job data.
    internal static IQuartzBuilder AddProcessOutboxMessagesJob<TDbContext>(
        IQuartzBuilder quartz,
        int processBatchSize,
        int processIntervalInSeconds,
        int delayInSecondsBeforeStart,
        OutboxRetryLimits retryLimits,
        int processedRetentionInDays,
        IOutboxLaneClaim? claim,
        TimeProvider? timeProvider)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(quartz);
        ArgumentNullException.ThrowIfNull(retryLimits);
        var time = timeProvider ?? TimeProvider.System;
        quartz.Services.AddOutboxMessageContext();
        // A concurrency conflict, a serialization failure or a deadlock: transient, so a failed try gets the transient tries.
        quartz.Services.AddTransientFailureRule<PersistenceTransientFailureRule>();
        quartz.Services.TryAddSingleton<OutboxWakeUp<TDbContext>>();
        quartz.Services.RemoveAll<OutboxJobClaim<TDbContext>>();
        quartz.Services.AddSingleton(new OutboxJobClaim<TDbContext>(claim));
        // The backlog monitor the outbox gauges and health check (AddOutbox<TDbContext>) read, measuring every 30 s;
        // AddOutboxProcessing replaces the interval with its BacklogCheckIntervalInSeconds.
        quartz.Services.AddOutboxBacklogMonitor(
            new OutboxBacklogSettings<TDbContext>(
                retryLimits.ReadLimit,
                OutboxBacklogMonitorRegistration.DefaultInterval),
            replace: false);
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
                    retryLimits.MaxRetryCount)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessMaxTransientRetryCountKey,
                    retryLimits.MaxTransientRetryCount)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessIntervalInSecondsKey,
                    processIntervalInSeconds)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessRetryDelayInMillisecondsKey,
                    (long)retryLimits.Retries.FirstDelay.TotalMilliseconds)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessMaxRetryDelayInMillisecondsKey,
                    (long)retryLimits.Retries.MaxDelay.TotalMilliseconds)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessRetryJitterKey,
                    retryLimits.Retries.Jitter)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessTransientRetryDelayInMillisecondsKey,
                    (long)retryLimits.TransientRetries.FirstDelay.TotalMilliseconds)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessMaxTransientRetryDelayInMillisecondsKey,
                    (long)retryLimits.TransientRetries.MaxDelay.TotalMilliseconds)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessTransientRetryJitterKey,
                    retryLimits.TransientRetries.Jitter))
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
