using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Whether every scheduled Quartz job still fires: a job that hasn't fired or finished (see
/// <see cref="JobHeartbeatRegistry"/>) for longer than <c>maxSilence(interval)</c> fails the check. A job's interval is
/// read from its triggers (the time between their next two fires), so it covers simple and cron schedules alike;
/// a job without a repeating, active trigger (paused, finished, a one-off) isn't checked.
/// </summary>
/// <remarks>
/// Meant for <c>/health/live</c>: a scheduler that stopped firing is fixed by a restart, which re-arms it. Before a
/// job's first fire its silence counts from when it was due to start (the process start, or its trigger's start time
/// when later), so a start-up delay isn't a stall.
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
            if (await ScheduleOfAsync(scheduler, job, now, cancellationToken) is not { } schedule)
                continue;

            var since = registry.LastBeat(job) ?? Later(registry.StartedAt, schedule.FirstFire);
            var silence = now - since;
            var limit = maxSilence(schedule.Interval);
            data[job.ToString()] = Math.Round(Math.Max(0, silence.TotalSeconds));
            if (silence > limit)
                stalled.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{job} last fired {silence:g} ago (every {schedule.Interval:g}, limit {limit:g})"));
        }

        return stalled.Count == 0
            ? HealthCheckResult.Healthy($"{data.Count} scheduled job(s) fire within their windows.", data)
            : new HealthCheckResult(
                context.Registration.FailureStatus,
                "Stalled Quartz job(s): " + string.Join("; ", stalled),
                data: data);
    }

    /// <summary>The shortest interval among the job's repeating, active triggers, and the earliest start among them.</summary>
    private static async Task<JobSchedule?> ScheduleOfAsync(
        IScheduler scheduler,
        JobKey job,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        JobSchedule? schedule = null;
        foreach (var trigger in await scheduler.GetTriggersOfJob(job, cancellationToken))
        {
            var state = await scheduler.GetTriggerState(trigger.Key, cancellationToken);
            if (state is TriggerState.Paused or TriggerState.Complete or TriggerState.None)
                continue;
            if (trigger.GetFireTimeAfter(now) is not { } next || trigger.GetFireTimeAfter(next) is not { } afterNext)
                continue;

            var found = new JobSchedule(afterNext - next, trigger.StartTimeUtc);
            schedule = schedule is null ? found : Tightest(schedule, found);
        }

        return schedule;
    }

    /// <summary>The shorter interval and the earlier start of two triggers' schedules.</summary>
    private static JobSchedule Tightest(
        JobSchedule first,
        JobSchedule second)
        => new(
            first.Interval < second.Interval ? first.Interval : second.Interval,
            first.FirstFire < second.FirstFire ? first.FirstFire : second.FirstFire);

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second)
        => first > second ? first : second;

    private sealed record JobSchedule(TimeSpan Interval, DateTimeOffset FirstFire);
}
