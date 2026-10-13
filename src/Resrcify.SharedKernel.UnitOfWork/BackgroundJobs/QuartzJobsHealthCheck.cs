using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Whether every scheduled Quartz job still fires: a job whose next fire after its last one (see
/// <see cref="JobHeartbeatRegistry"/>) is overdue by more than its allowance fails the check. The allowance is
/// <c>maxSilence(interval)</c> less one interval, the interval being the gap from that fire to the one after it, so
/// uneven schedules (business hours, weekdays, fixed times) are judged by the fire that is actually due, not by the
/// short gaps of another part of the day. A job without a repeating, active trigger (paused, finished, a one-off)
/// isn't checked.
/// </summary>
/// <remarks>
/// Meant for <c>/health/live</c>: a scheduler that stopped firing is fixed by a restart, which re-arms it. Before a
/// job's first fire its silence counts from when it was due to start (the process start, or its trigger's start time
/// when later), so a start-up delay isn't a stall. On an even schedule this is "silent for longer than
/// <c>maxSilence(interval)</c>".
/// </remarks>
internal sealed class QuartzJobsHealthCheck(
    JobHeartbeatRegistry registry,
    ISchedulerFactory schedulerFactory,
    Func<TimeSpan, TimeSpan> maxSilence,
    TimeProvider time)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        if (scheduler.Status is SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown)
            return HealthCheckResult.Healthy("The scheduler is shut down.");

        var now = time.GetUtcNow();
        var stalled = new List<string>();
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var job in await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken))
        {
            var lastBeat = registry.LastBeat(job);
            if (await DueFireOfAsync(scheduler, job, lastBeat, cancellationToken) is not { } due)
                continue;

            var silence = now - (lastBeat ?? due.Since);
            data[job.ToString()] = Math.Round(Math.Max(0, silence.TotalSeconds));
            var limit = maxSilence(due.Interval);
            var allowance = limit > due.Interval ? limit - due.Interval : TimeSpan.Zero;
            if (now - due.At > allowance)
                stalled.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{job} last fired {silence:g} ago, was due {now - due.At:g} ago (every {due.Interval:g}, limit {limit:g})"));
        }

        return stalled.Count == 0
            ? HealthCheckResult.Healthy($"{data.Count} scheduled job(s) fire within their windows.", data)
            : new HealthCheckResult(
                context.Registration.FailureStatus,
                "Stalled Quartz job(s): " + string.Join("; ", stalled),
                data: data);
    }

    /// <summary>
    /// The earliest fire the job's repeating, active triggers have due after its last beat (or, before its first, after
    /// it was due to start), with the gap to the fire after it.
    /// </summary>
    private async Task<DueFire?> DueFireOfAsync(
        IScheduler scheduler,
        JobKey job,
        DateTimeOffset? lastBeat,
        CancellationToken cancellationToken)
    {
        DueFire? earliest = null;
        foreach (var trigger in await scheduler.GetTriggersOfJob(job, cancellationToken))
        {
            var state = await scheduler.GetTriggerState(trigger.Key, cancellationToken);
            if (state is TriggerState.Paused or TriggerState.Complete or TriggerState.None)
                continue;

            // Before a first fire: from the start, including a fire right at it.
            var since = lastBeat ?? Later(registry.StartedAt, trigger.StartTimeUtc);
            var after = lastBeat ?? since.AddTicks(-1);
            if (trigger.GetFireTimeAfter(after) is not { } at || trigger.GetFireTimeAfter(at) is not { } next)
                continue;

            if (earliest is null || at < earliest.At)
                earliest = new DueFire(at, next - at, since);
        }

        return earliest;
    }

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second)
        => first > second ? first : second;

    /// <summary>When the job is due to fire next, the gap to the fire after that, and since when it has been silent.</summary>
    private sealed record DueFire(DateTimeOffset At, TimeSpan Interval, DateTimeOffset Since);
}
