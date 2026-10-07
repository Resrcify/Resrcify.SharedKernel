using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Runs every outbox lane (see <see cref="OutboxLaneRegistry"/>): one loop per lane, each
/// keeping up to <see cref="OutboxLaneOptions.MaxConcurrency"/> of its messages in flight and starting
/// the next as soon as one finishes. A lane polls every <see cref="OutboxLaneOptions.PollInterval"/>, and at once
/// when one of its messages finishes (a slot freed) or the outbox is woken (<see cref="OutboxWakeUp{TDbContext}"/>).
/// The regular outbox job skips these event types.
/// </summary>
/// <remarks>
/// A failed message is tried again after 5 s, then 10 s, 20 s, … up to 5 min. That schedule is kept in the database
/// (<see cref="OutboxMessage.NextAttemptOnUtc"/>, written with the failure), not in this instance, so several
/// instances running the lanes don't spend a message's tries in a few seconds of an outage, however long it waited
/// before its first try.
/// </remarks>
internal sealed partial class OutboxLanesHost<TDbContext>(
    OutboxLaneRegistry registry,
    OutboxLaneSettings<TDbContext> settings,
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxLanesHost<TDbContext>> logger,
    TimeProvider? timeProvider = null,
    OutboxWakeUp<TDbContext>? wakeUp = null)
    : BackgroundService
    where TDbContext : DbContext
{
    /// <summary>The first retry of a failed message waits this long; each further one twice as long.</summary>
    private static TimeSpan FirstRetryDelay => TimeSpan.FromSeconds(5);

    /// <summary>The longest a failed message waits before its next attempt.</summary>
    private static TimeSpan MaxRetryDelay => TimeSpan.FromMinutes(5);

    private readonly OutboxLaneOptions options = settings.Options;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Messages another instance held, and when this one may look at them again.</summary>
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _backingOff = new();

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(registry.Lanes.Select(lane => RunLaneAsync(lane.Key, lane.Value, stoppingToken)));

    private async Task RunLaneAsync(string lane, IReadOnlyList<string> eventTypes, CancellationToken stoppingToken)
    {
        LogLaneStarted(lane, eventTypes.Count, options.MaxConcurrency);
        var inFlight = new ConcurrentDictionary<Guid, Task>();
        var signal = new WakeSignal();
        using var wakeUps = wakeUp?.WakeLane(signal);
        try
        {
            while (true)
            {
                try
                {
                    await StartDueMessagesAsync(eventTypes, inFlight, signal, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogPollFailed(exception, lane);
                }

                await signal.WaitAsync(options.PollInterval, _time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: in-flight messages roll back and are picked up again after restart.
        }
        finally
        {
            await Task.WhenAll(inFlight.Values);
        }
    }

    private async Task StartDueMessagesAsync(
        IReadOnlyList<string> eventTypes,
        ConcurrentDictionary<Guid, Task> inFlight,
        WakeSignal signal,
        CancellationToken stoppingToken)
    {
        var freeSlots = options.MaxConcurrency - inFlight.Count;
        if (freeSlots <= 0)
            return;

        var now = _time.GetUtcNow();
        var nowUtc = now.UtcDateTime;
        foreach (var (id, notBefore) in _backingOff)
            if (notBefore <= now)
                _backingOff.TryRemove(id, out _);
        var busy = inFlight.Keys.Concat(_backingOff.Keys).ToList();
        List<OutboxMessageToProcess> due;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<TDbContext>()
                .Set<OutboxMessage>()
                .Where(m =>
                    m.ProcessedOnUtc == null &&
                    m.RetryCount < options.MaxRetryCount &&
                    eventTypes.Contains(m.Type) &&
                    !busy.Contains(m.Id) &&
                    (m.NextAttemptOnUtc == null || m.NextAttemptOnUtc <= nowUtc))
                .OrderBy(m => m.OccurredOnUtc)
                .Take(freeSlots)
                .Select(m => new OutboxMessageToProcess(m.Id, m.Type, m.Content, m.RetryCount, m.OccurredOnUtc))
                .ToListAsync(stoppingToken);
        }

        foreach (var message in due)
        {
            // Recorded before it starts, so it can't finish (and remove itself) before it is recorded.
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            inFlight[message.Id] = ProcessAndReleaseAsync(message, inFlight, signal, start.Task, stoppingToken);
            start.SetResult();
        }
    }

    private async Task ProcessAndReleaseAsync(
        OutboxMessageToProcess message,
        ConcurrentDictionary<Guid, Task> inFlight,
        WakeSignal signal,
        Task started,
        CancellationToken stoppingToken)
    {
        await started;
        try
        {
            var outcome = await OutboxMessageProcessor<TDbContext>.ProcessAsync(
                scopeFactory,
                message,
                options.Claim,
                options.MaxRetryCount,
                RetryDelay(message.RetryCount + 1),
                stoppingToken);
            // Another instance holds it (a scatter-gather may hold it a while): look again at the next poll, not at
            // once, or the freed slot would claim it again and again. A failed one waits in the database.
            if (outcome == OutboxProcessOutcome.Skipped)
                _backingOff[message.Id] = _time.GetUtcNow() + options.PollInterval;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; the message stays unprocessed.
        }
        catch (Exception exception)
        {
            LogMessageFailed(exception, message.Id);
        }
        finally
        {
            inFlight.TryRemove(message.Id, out _);
            // A slot is free: the lane looks for its next message now rather than at its next poll.
            signal.Set();
        }
    }

    /// <summary>5 s after the first failure, then 10 s, 20 s, … up to <see cref="MaxRetryDelay"/>.</summary>
    internal static TimeSpan RetryDelay(int failures)
    {
        // Doubled at most until past the cap: 5 s doubled 39 times overflows a TimeSpan.
        var delay = FirstRetryDelay;
        for (var doubled = 1; doubled < failures && delay < MaxRetryDelay; doubled++)
            delay *= 2;
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }


    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox lane {Lane} started for {EventTypes} event type(s), {MaxConcurrency} at a time")]
    private partial void LogLaneStarted(string lane, int eventTypes, int maxConcurrency);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox lane {Lane} failed to poll; it tries again next interval")]
    private partial void LogPollFailed(Exception exception, string lane);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox lane message {MessageId} failed outside its handlers")]
    private partial void LogMessageFailed(Exception exception, Guid messageId);
}
