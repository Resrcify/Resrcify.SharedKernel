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
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
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
/// A failed message is tried again by its failure's schedule (<see cref="OutboxLaneOptions.Retries"/>, or
/// <see cref="OutboxLaneOptions.TransientRetries"/> for a transient failure): after 5 s, then twice as long each time
/// (10 s, 20 s, …) up to 5 min by default. That schedule is kept in the database
/// (<see cref="OutboxMessage.NextAttemptOnUtc"/>, written with the failure), not in this instance, so several
/// instances running the lanes don't spend a message's tries in a few seconds of an outage, however long it waited
/// before its first try.
/// </remarks>
internal sealed partial class OutboxLanesHost<TDbContext>(
    OutboxLaneRegistry registry,
    OutboxLaneSettings<TDbContext> settings,
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxLanesHost<TDbContext>> logger,
    TimeProvider timeProvider,
    IServiceProvider services,
    OutboxWakeUp<TDbContext>? wakeUp = null)
    : BackgroundService
    where TDbContext : DbContext
{
    private readonly OutboxLaneOptions options = settings.Options;

    // The lanes' own claim, else the outbox job's (AddOutboxLanes), else the one the package writing the outbox gives.
    private readonly IOutboxLaneClaim? _claim = OutboxDefaultClaim<TDbContext>.Effective(settings.Options.Claim, services);
    private readonly TimeProvider _time = timeProvider;

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
        // The higher of the two limits: a message retrying transiently past MaxRetryCount is still read.
        var retryLimit = settings.RetryLimits.ReadLimit;
        List<OutboxMessageToProcess> due;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<TDbContext>()
                .Set<OutboxMessage>()
                .Where(m =>
                    m.ProcessedOnUtc == null &&
                    m.RetryCount < retryLimit &&
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
                _claim,
                settings.RetryLimits,
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox lane {Lane} started for {EventTypes} event type(s), {MaxConcurrency} at a time")]
    private partial void LogLaneStarted(string lane, int eventTypes, int maxConcurrency);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox lane {Lane} failed to poll; it tries again next interval")]
    private partial void LogPollFailed(Exception exception, string lane);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox lane message {MessageId} failed outside its handlers")]
    private partial void LogMessageFailed(Exception exception, Guid messageId);
}
