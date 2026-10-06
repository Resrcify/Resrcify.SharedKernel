using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Wakes <typeparamref name="TDbContext"/>'s outbox now instead of at its next poll: runs the processing job
/// (<see cref="OutboxJobs.Process{TDbContext}"/>) and lets every outbox lane poll. Polling stays the safety net; this
/// only shortens the wait. Registered with the outbox job and the lanes; on PostgreSQL <c>WithOutboxWakeUp()</c> calls
/// it when a save commits outbox messages.
/// </summary>
/// <remarks>
/// Cheap to call often. Wake-ups coalesce: while a run of the job is already on its way (triggered, not started yet),
/// another wake-up doesn't trigger one more, so a burst of wake-ups costs at most one run more than it needs (the run
/// started meanwhile drains the outbox while its batches come back full).
/// </remarks>
public sealed partial class OutboxWakeUp<TDbContext>(
    IServiceProvider provider,
    ILogger<OutboxWakeUp<TDbContext>> logger)
    where TDbContext : DbContext
{
    private readonly ConcurrentDictionary<WakeSignal, byte> _lanes = new();
    private int _runPending;

    /// <summary>Lets the outbox lanes poll now, and runs the processing job unless a run is already on its way.</summary>
    public async Task WakeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var lane in _lanes.Keys)
            lane.Set();

        await TriggerJobAsync(cancellationToken);
    }

    /// <summary>Called by the job when a run starts: a wake-up after this triggers another run.</summary>
    internal void RunStarted()
        => Volatile.Write(ref _runPending, 0);

    /// <summary>Wakes <paramref name="lane"/> on every wake-up, until the returned registration is disposed.</summary>
    internal Registration WakeLane(WakeSignal lane)
    {
        _lanes[lane] = 0;
        return new Registration(this, lane);
    }

    private async Task TriggerJobAsync(CancellationToken cancellationToken)
    {
        if (provider.GetService<ISchedulerFactory>() is not { } schedulers)
            return;
        if (Interlocked.Exchange(ref _runPending, 1) == 1)
            return;

        var triggered = false;
        try
        {
            var scheduler = await schedulers.GetScheduler(cancellationToken);
            var job = OutboxJobs.Process<TDbContext>();
            if (scheduler.Status is not (SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown)
                && await scheduler.Exists(job, cancellationToken))
            {
                await scheduler.TriggerJob(job, data: null, cancellationToken);
                triggered = true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogTriggerFailed(exception, typeof(TDbContext).Name);
        }
        finally
        {
            if (!triggered)
                RunStarted();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not wake the {DbContext} outbox job; its next poll processes the messages")]
    private partial void LogTriggerFailed(Exception exception, string dbContext);

    /// <summary>Stops waking a lane.</summary>
    internal readonly struct Registration(
        OutboxWakeUp<TDbContext> wakeUp,
        WakeSignal lane)
        : IDisposable
    {
        public void Dispose()
            => wakeUp._lanes.TryRemove(lane, out _);
    }
}
