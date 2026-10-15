using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Processes <typeparamref name="TDbContext"/>'s outbox, in order of occurrence, a batch at a time.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A run keeps reading batches while the last one came back full, so a backlog drains in one run instead of a
/// batch per trigger. It stops starting batches after <see cref="DrainBudgetShare"/> of the trigger's interval, so a
/// run ends before the next trigger is due (one batch may run past it). A quiet outbox costs one query per run.</item>
/// <item>Each message is processed in its own scope and transaction, as before. A message that fails (or that another
/// instance holds) is left for the next run: a run never reads it twice, so its retries keep their pace.</item>
/// <item>A message whose try failed waits before its next one, as in the outbox lanes: 5 s, then twice as long each
/// time, up to 5 min (<c>OutboxOptions.Retries</c>, or <c>TransientRetries</c>). The time is written to
/// <see cref="OutboxMessage.NextAttemptOnUtc"/> with the failure, and a run reads only the messages that are due, so a
/// handler failing through an outage of a few minutes doesn't spend its tries in the first minute of it.</item>
/// <item>A transient failure (a timeout, an HTTP 5xx, a transient database failure) gets more tries than any other:
/// <c>OutboxOptions.TransientRetries</c> (12 attempts, about 30 minutes with the default delays, or <c>Retries</c>'
/// when that is higher) against <c>Retries</c> (3).</item>
/// </list>
/// </remarks>
[DisallowConcurrentExecution]
public sealed class ProcessOutboxMessagesJob<TDbContext>(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    OutboxWakeUp<TDbContext>? wakeUp = null)
    : IJob
    where TDbContext : DbContext
{
    internal const string ProcessBatchSizeKey = "ProcessBatchSize";
    internal const string ProcessMaxRetryCountKey = "ProcessMaxRetryCount";
    internal const string ProcessMaxTransientRetryCountKey = "ProcessMaxTransientRetryCount";
    internal const string ProcessIntervalKey = "ProcessIntervalInMilliseconds";

    /// <summary>The interval as 4.2 and before stored it, in whole seconds: still read from a persistent store.</summary>
    internal const string LegacyProcessIntervalInSecondsKey = "ProcessIntervalInSeconds";
    internal const string ProcessRetryDelayInMillisecondsKey = "ProcessRetryDelayInMilliseconds";
    internal const string ProcessMaxRetryDelayInMillisecondsKey = "ProcessMaxRetryDelayInMilliseconds";
    internal const string ProcessRetryJitterKey = "ProcessRetryJitter";
    internal const string ProcessTransientRetryDelayInMillisecondsKey = "ProcessTransientRetryDelayInMilliseconds";
    internal const string ProcessMaxTransientRetryDelayInMillisecondsKey = "ProcessMaxTransientRetryDelayInMilliseconds";
    internal const string ProcessTransientRetryJitterKey = "ProcessTransientRetryJitter";
    internal const int DefaultProcessBatchSize = 20;
    internal static TimeSpan DefaultProcessInterval => TimeSpan.FromSeconds(60);

    /// <summary>The share of the trigger's interval a run spends starting batches: 80%.</summary>
    public const double DrainBudgetShare = 0.8;

    private readonly TimeProvider _time = timeProvider;

    public async ValueTask Execute(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Wake-ups from now on trigger another run: what this run doesn't read, that one does.
        wakeUp?.RunStarted();

        var batchSize = ReadInt(
            context.MergedJobDataMap,
            ProcessBatchSizeKey,
            DefaultProcessBatchSize);
        var limits = ReadRetryLimits(context.MergedJobDataMap);
        var budget = DrainBudget(context);
        var started = _time.GetTimestamp();

        IOutboxLaneClaim? claim;
        await using (var scope = scopeFactory.CreateAsyncScope())
            claim = OutboxDefaultClaim<TDbContext>.Effective(
                scope.ServiceProvider.GetService<OutboxJobClaim<TDbContext>>()?.Claim,
                scope.ServiceProvider);

        // Failed (or held by another instance) in this run: left for the next one.
        var passedOver = new List<Guid>();
        while (true)
        {
            var messages = await ReadBatchAsync(
                batchSize,
                limits.ReadLimit,
                passedOver,
                cancellationToken);

            foreach (var message in messages)
            {
                var outcome = await OutboxMessageProcessor<TDbContext>.ProcessAsync(
                    scopeFactory,
                    message,
                    claim,
                    limits,
                    cancellationToken);
                if (outcome != OutboxProcessOutcome.Processed)
                    passedOver.Add(message.Id);
            }

            if (messages.Count < batchSize || _time.GetElapsedTime(started) >= budget)
                return;
        }
    }

    /// <summary>
    /// How long a run starts batches: <see cref="DrainBudgetShare"/> of the interval the setup stored, else of the
    /// trigger's repeat interval, else of 60 seconds.
    /// </summary>
    private static TimeSpan DrainBudget(IJobExecutionContext context)
    {
        var interval = JobDataDurations.Read(
            context.MergedJobDataMap,
            ProcessIntervalKey,
            LegacyProcessIntervalInSecondsKey,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero);
        if (interval > TimeSpan.Zero)
            return interval * DrainBudgetShare;

        if (context.Trigger is ISimpleTrigger { RepeatInterval: var repeat } && repeat > TimeSpan.Zero)
            return repeat * DrainBudgetShare;

        return DefaultProcessInterval * DrainBudgetShare;
    }

    // The schedules a message gets, made as every registration makes them (OutboxRetryLimits.Create). A job stored
    // without the transient attempts (before 4.3.0, in a persistent store) leaves them unset: 12, or ProcessMaxRetryCount
    // when higher; one stored without the transient delays takes the regular ones.
    private static OutboxRetryLimits ReadRetryLimits(JobDataMap data)
    {
        var defaults = OutboxRetryLimits.DefaultRetries;
        var retries = new RetrySchedule(
            ReadInt(data, ProcessMaxRetryCountKey, defaults.MaxAttempts),
            ReadDelay(data, ProcessRetryDelayInMillisecondsKey, defaults.FirstDelay),
            ReadDelay(data, ProcessMaxRetryDelayInMillisecondsKey, defaults.MaxDelay),
            ReadBool(data, ProcessRetryJitterKey, defaults.Jitter));
        var transientRetries = ReadOptionalInt(data, ProcessMaxTransientRetryCountKey) is { } transientAttempts
            ? new RetrySchedule(
                transientAttempts,
                ReadDelay(data, ProcessTransientRetryDelayInMillisecondsKey, retries.FirstDelay),
                ReadDelay(data, ProcessMaxTransientRetryDelayInMillisecondsKey, retries.MaxDelay),
                ReadBool(data, ProcessTransientRetryJitterKey, retries.Jitter))
            : null;
        return OutboxRetryLimits.Create(
            retries,
            transientRetries,
            ProcessMaxRetryCountKey,
            ProcessMaxTransientRetryCountKey);
    }

    // A flag stored as a bool or a string; anything else falls back.
    private static bool ReadBool(JobDataMap data, string key, bool fallback)
        => data.TryGetValue(key, out var value)
            ? value switch
            {
                bool flag => flag,
                string text when bool.TryParse(text, out var parsed) => parsed,
                _ => fallback,
            }
            : fallback;

    // A delay stored in milliseconds (an int, a long or a string, or a TimeSpan); a negative or unusable one falls back.
    private static TimeSpan ReadDelay(JobDataMap data, string key, TimeSpan fallback)
    {
        if (!data.TryGetValue(key, out var value))
            return fallback;

        long? milliseconds = value switch
        {
            int number => number,
            long number => number,
            TimeSpan delay => (long)delay.TotalMilliseconds,
            string text when long.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
        return milliseconds is >= 0
            ? TimeSpan.FromMilliseconds(milliseconds.Value)
            : fallback;
    }

    // Quartz 4 dropped the typed JobDataMap accessors (TryGetInt and friends); the map
    // only hands back the stored object. The setup stores ints, but a value that came in
    // as a long or a string (configuration, a persistent store) is accepted too, and
    // anything unusable falls back to the default rather than failing the job.
    private static int ReadInt(JobDataMap data, string key, int fallback)
        => ReadOptionalInt(data, key) ?? fallback;

    // An int stored under the key, or null when there is none or it is unusable.
    private static int? ReadOptionalInt(JobDataMap data, string key)
    {
        if (!data.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            int number => number,
            long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            string text when int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }

    // No-tracking projection — only the columns the job needs, so the covering index
    // can serve the read. Messages that gave up have a ProcessedOnUtc (OutboxMessage.GivenUpProcessedOnUtc), so the
    // read doesn't meet them; the retry limit still skips one that gave up before they were marked. The limit is
    // OutboxRetryLimits.ReadLimit, the higher of the two: a message retrying transiently past MaxRetryCount is still read.
    // Event types that belong to an outbox lane are skipped too: the lane processes those itself. So is a message
    // waiting for a later try (NextAttemptOnUtc ahead): a residual filter on the unprocessed rows the partial index
    // narrows the read to, as in the lanes' read.
    private async Task<List<OutboxMessageToProcess>> ReadBatchAsync(
        int batchSize,
        int retryLimit,
        List<Guid> passedOver,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var query = context
            .Set<OutboxMessage>()
            .Where(m =>
                m.ProcessedOnUtc == null &&
                m.RetryCount < retryLimit &&
                (m.NextAttemptOnUtc == null || m.NextAttemptOnUtc <= nowUtc));

        var laneTypes = scope.ServiceProvider.GetService<OutboxLaneRegistry>()?.LaneEventTypes ?? [];
        if (laneTypes.Count > 0)
            query = query.Where(m => !laneTypes.Contains(m.Type));
        if (passedOver.Count > 0)
            query = query.Where(m => !passedOver.Contains(m.Id));

        return await query
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize)
            .Select(m => new OutboxMessageToProcess(
                m.Id,
                m.Type,
                m.Content,
                m.RetryCount,
                m.OccurredOnUtc))
            .ToListAsync(cancellationToken);
    }
}
