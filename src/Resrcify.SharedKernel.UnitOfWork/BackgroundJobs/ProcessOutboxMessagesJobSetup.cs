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
/// <c>delayBeforeStart</c> still counts from scheduler start-up, as it did before.
/// <para>
/// <see cref="AddProcessOutboxMessagesJob{TDbContext}"/> also registers the backlog monitor (a hosted service measuring
/// the outbox every 30 s) that the <c>outbox.messages.*</c> gauges and the <c>AddOutbox&lt;TDbContext&gt;</c> health
/// check read, as <c>AddOutboxProcessing</c> does, so the health check works with either registration.
/// </para>
/// </remarks>
public static class ProcessOutboxMessagesJobSetup
{
    /// <summary>How long after start-up the processing job first runs unless told otherwise: 60 s.</summary>
    public static readonly TimeSpan DefaultDelayBeforeStart = TimeSpan.FromSeconds(60);

    /// <param name="quartz">The Quartz builder.</param>
    /// <param name="processBatchSize">Messages read per run.</param>
    /// <param name="processInterval">
    /// Time between runs (60 s when <see langword="null"/>; at least 1 s). A run drains a backlog (it reads batch after
    /// batch while they come back full) for up to 80% of this.
    /// </param>
    /// <param name="delayBeforeStart">Time after start-up before the first run (60 s when <see langword="null"/>).</param>
    /// <param name="retries">
    /// The attempts a message gets before it is poison, when a try fails for a reason another try won't change, and the
    /// waits between them; <see langword="null"/>: 3 attempts, 5 s after the first failure, then twice as long each time,
    /// up to 5 min.
    /// </param>
    /// <param name="processedRetention">
    /// How long processed messages are kept (an hourly cleanup); 7 days when <see langword="null"/>,
    /// <see cref="TimeSpan.Zero"/> turns it off.
    /// </param>
    /// <param name="claim">
    /// Claims each message for its processing transaction, so several service instances don't each publish it.
    /// <see langword="null"/>: the PostgreSQL claim when the context writes its outbox through the Postgres package
    /// (<c>WithOutbox()</c>), no claim otherwise (fine for one instance).
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
    /// <paramref name="transientRetries"/> is set with fewer attempts than <paramref name="retries"/>, or
    /// <paramref name="processInterval"/> is under 1 s, or <paramref name="delayBeforeStart"/> or
    /// <paramref name="processedRetention"/> negative.
    /// </exception>
    public static IQuartzBuilder AddProcessOutboxMessagesJob<TDbContext>(
        this IQuartzBuilder quartz,
        int processBatchSize = ProcessOutboxMessagesJob<DbContext>.DefaultProcessBatchSize,
        TimeSpan? processInterval = null,
        TimeSpan? delayBeforeStart = null,
        RetrySchedule? retries = null,
        TimeSpan? processedRetention = null,
        IOutboxLaneClaim? claim = null,
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
            processInterval ?? ProcessOutboxMessagesJob<DbContext>.DefaultProcessInterval,
            delayBeforeStart ?? DefaultDelayBeforeStart,
            limits,
            processedRetention ?? CleanupOutboxMessagesJob<DbContext>.DefaultRetention,
            claim);
    }

    // The registration, with the retry limits its callers made (OutboxRetryLimits.Create): the attempts, delays and
    // jitter of both schedules go in the job data.
    internal static IQuartzBuilder AddProcessOutboxMessagesJob<TDbContext>(
        IQuartzBuilder quartz,
        int processBatchSize,
        TimeSpan processInterval,
        TimeSpan delayBeforeStart,
        OutboxRetryLimits retryLimits,
        TimeSpan processedRetention,
        IOutboxLaneClaim? claim)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(quartz);
        ArgumentNullException.ThrowIfNull(retryLimits);
        ArgumentOutOfRangeException.ThrowIfLessThan(processInterval, TimeSpan.FromSeconds(1));
        ArgumentOutOfRangeException.ThrowIfLessThan(delayBeforeStart, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(processedRetention, TimeSpan.Zero);
        // The clock the jobs read and their first runs are computed from, the container's (as Quartz 4 runs the
        // scheduler on it); a later registration (e.g. a FakeTimeProvider in a test) replaces it.
        quartz.Services.TryAddSingleton(TimeProvider.System);
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
        if (processedRetention > TimeSpan.Zero)
            quartz.AddCleanupOutboxMessagesJob<TDbContext>(processedRetention);

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
                    ProcessOutboxMessagesJob<TDbContext>.ProcessIntervalKey,
                    (long)processInterval.TotalMilliseconds)
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
            .AddTrigger<ProcessOutboxMessagesJob<TDbContext>>((provider, trigger) => trigger
                .ForJob(jobKey)
                .StartAt(provider.GetRequiredService<TimeProvider>().GetUtcNow() + delayBeforeStart)
                .WithSimpleSchedule(schedule => schedule
                    .WithInterval(processInterval)
                    .RepeatForever()));
    }

    /// <summary>
    /// Deletes processed outbox messages older than <paramref name="processedRetention"/> (7 days when
    /// <see langword="null"/>) every hour (first run five minutes after start-up).
    /// <see cref="AddProcessOutboxMessagesJob{TDbContext}"/> adds it, with 7 days.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="processedRetention"/> isn't positive.</exception>
    public static IQuartzBuilder AddCleanupOutboxMessagesJob<TDbContext>(
        this IQuartzBuilder quartz,
        TimeSpan? processedRetention = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(quartz);
        var retention = processedRetention ?? CleanupOutboxMessagesJob<DbContext>.DefaultRetention;
        if (retention <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(processedRetention), processedRetention, "The retention must be positive.");

        quartz.Services.TryAddSingleton(TimeProvider.System);
        var jobKey = OutboxJobs.Cleanup<TDbContext>();
        return quartz
            .AddJob<CleanupOutboxMessagesJob<TDbContext>>(job => job
                .WithIdentity(jobKey)
                .UsingJobData(CleanupOutboxMessagesJob<TDbContext>.RetentionKey, (long)retention.TotalMilliseconds))
            .AddTrigger<CleanupOutboxMessagesJob<TDbContext>>((provider, trigger) => trigger
                .ForJob(jobKey)
                .StartAt(provider.GetRequiredService<TimeProvider>().GetUtcNow().AddMinutes(5))
                .WithSimpleSchedule(schedule => schedule
                    .WithInterval(TimeSpan.FromHours(1))
                    .RepeatForever()));
    }
}
