using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>A liveness check for the Quartz jobs.</summary>
public static class QuartzJobsHealthChecksBuilderExtensions
{
    /// <summary>
    /// How long a job may go without firing, by default: three of its intervals plus two minutes (a 1-minute job 5
    /// minutes, a 15-minute job 47, a 6-hour job 18 hours).
    /// </summary>
    public static readonly Func<TimeSpan, TimeSpan> DefaultMaxSilence = interval => interval * 3 + TimeSpan.FromMinutes(2);

    /// <summary>
    /// Fails (<see cref="HealthStatus.Unhealthy"/> by default) when a scheduled job hasn't fired or finished for longer
    /// than <paramref name="maxSilence"/> of its interval: the scheduler stopped firing, which a restart fixes, so
    /// register it for the liveness endpoint (<c>tags: ["live"]</c>, the Web package's <c>HealthTags.Live</c>). A job
    /// that fires and fails isn't stalled. A run counts as silence until it ends, so give long-running jobs room
    /// (<c>maxSilence: interval => interval * 3 + TimeSpan.FromMinutes(30)</c>). Needs <c>AddJobHeartbeats()</c> on
    /// the Quartz builder.
    /// </summary>
    /// <example><c>services.AddHealthChecks().AddQuartzJobs(tags: ["live"]);</c></example>
    public static IHealthChecksBuilder AddQuartzJobs(
        this IHealthChecksBuilder builder,
        Func<TimeSpan, TimeSpan>? maxSilence = null,
        string name = "quartz-jobs",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var limit = maxSilence ?? DefaultMaxSilence;
        return builder.Add(new HealthCheckRegistration(
            name,
            provider => new QuartzJobsHealthCheck(
                provider.GetService<JobHeartbeatRegistry>()
                    ?? throw new InvalidOperationException(
                        "The Quartz jobs health check needs AddJobHeartbeats() on the Quartz builder (services.AddQuartz(q => q.AddJobHeartbeats()))."),
                provider.GetRequiredService<ISchedulerFactory>(),
                limit,
                provider.GetService<TimeProvider>() ?? TimeProvider.System),
            failureStatus,
            tags));
    }
}
