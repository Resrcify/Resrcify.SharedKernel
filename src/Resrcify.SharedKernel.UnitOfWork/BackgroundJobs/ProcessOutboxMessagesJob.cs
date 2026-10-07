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
/// </list>
/// </remarks>
[DisallowConcurrentExecution]
public sealed class ProcessOutboxMessagesJob<TDbContext>(
    IServiceScopeFactory scopeFactory,
    TimeProvider? timeProvider = null,
    OutboxWakeUp<TDbContext>? wakeUp = null)
    : IJob
    where TDbContext : DbContext
{
    internal const string ProcessBatchSizeKey = "ProcessBatchSize";
    internal const string ProcessMaxRetryCountKey = "ProcessMaxRetryCount";
    internal const string ProcessIntervalInSecondsKey = "ProcessIntervalInSeconds";
    internal const int DefaultProcessBatchSize = 20;
    internal const int DefaultProcessMaxRetryCount = 3;
    internal const int DefaultProcessIntervalInSeconds = 60;

    /// <summary>The share of the trigger's interval a run spends starting batches: 80%.</summary>
    public const double DrainBudgetShare = 0.8;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

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
        var maxRetryCount = ReadInt(
            context.MergedJobDataMap,
            ProcessMaxRetryCountKey,
            DefaultProcessMaxRetryCount);
        var budget = DrainBudget(context);
        var started = _time.GetTimestamp();

        IOutboxLaneClaim? claim;
        await using (var scope = scopeFactory.CreateAsyncScope())
            claim = scope.ServiceProvider.GetService<OutboxJobClaim<TDbContext>>()?.Claim;

        // Failed (or held by another instance) in this run: left for the next one.
        var passedOver = new List<Guid>();
        while (true)
        {
            var messages = await ReadBatchAsync(
                batchSize,
                maxRetryCount,
                passedOver,
                cancellationToken);

            foreach (var message in messages)
            {
                var outcome = await OutboxMessageProcessor<TDbContext>.ProcessAsync(
                    scopeFactory,
                    message,
                    claim,
                    maxRetryCount,
                    retryDelay: null,
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
        var intervalInSeconds = ReadInt(context.MergedJobDataMap, ProcessIntervalInSecondsKey, 0);
        if (intervalInSeconds > 0)
            return TimeSpan.FromSeconds(intervalInSeconds) * DrainBudgetShare;

        if (context.Trigger is ISimpleTrigger { RepeatInterval: var repeat } && repeat > TimeSpan.Zero)
            return repeat * DrainBudgetShare;

        return TimeSpan.FromSeconds(DefaultProcessIntervalInSeconds) * DrainBudgetShare;
    }

    // Quartz 4 dropped the typed JobDataMap accessors (TryGetInt and friends); the map
    // only hands back the stored object. The setup stores ints, but a value that came in
    // as a long or a string (configuration, a persistent store) is accepted too, and
    // anything unusable falls back to the default rather than failing the job.
    private static int ReadInt(JobDataMap data, string key, int fallback)
    {
        if (!data.TryGetValue(key, out var value))
            return fallback;

        return value switch
        {
            int number => number,
            long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            string text when int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => fallback
        };
    }

    // No-tracking projection — only the columns the job needs, so the covering index
    // can serve the read. Messages that gave up have a ProcessedOnUtc (OutboxMessage.GivenUpProcessedOnUtc), so the
    // read doesn't meet them; the retry limit still skips one that gave up before they were marked. Event types that
    // belong to an outbox lane are skipped too: the lane processes those itself.
    private async Task<List<OutboxMessageToProcess>> ReadBatchAsync(
        int batchSize,
        int maxRetryCount,
        List<Guid> passedOver,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var query = context
            .Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null && m.RetryCount < maxRetryCount);

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
