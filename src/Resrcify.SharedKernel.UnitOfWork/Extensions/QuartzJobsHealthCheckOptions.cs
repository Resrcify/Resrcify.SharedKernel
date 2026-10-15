using System;
using System.Collections.Generic;
using System.Linq;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>
/// Which Quartz jobs the <c>AddQuartzJobs</c> liveness check watches, and how long each may stay silent.
/// </summary>
/// <remarks>
/// The outbox's jobs (<see cref="OutboxJobs.Process{TDbContext}"/>, <see cref="OutboxJobs.Cleanup{TDbContext}"/>) are
/// left out unless <see cref="WatchOutboxJobs"/> is set: an outbox stalls on its broker or its database, which a restart
/// doesn't fix, so watching it on <c>/health/live</c> turns an outage into a restart loop of every replica. The outbox has
/// its own signal for a stall: the <c>AddOutbox&lt;TDbContext&gt;</c> check (the oldest waiting message's age, Degraded on
/// <c>/health/ready</c>) and the <c>outbox.messages.*</c> gauges. A scheduler that stopped firing altogether is still
/// caught by every other job.
/// </remarks>
/// <example>
/// <code>
/// services.AddHealthChecks().AddQuartzJobs(
///     jobs => jobs
///         .Exclude&lt;ImportArchiveJob&gt;()   // runs for hours: its own alert, not a restart
///         .Exclude(job => job.Group == "reports"));
/// </code>
/// </example>
public sealed class QuartzJobsHealthCheckOptions
{
    private readonly List<Func<JobKey, bool>> _excluded = [];

    /// <summary>
    /// How long a job may go without firing or finishing, given its interval;
    /// <see cref="QuartzJobsHealthChecksBuilderExtensions.DefaultMaxSilence"/> (three intervals plus two minutes) unless
    /// set. A job the check shouldn't watch at all is better excluded (<see cref="Exclude(JobKey)"/>) than given an
    /// endless window.
    /// </summary>
    public Func<TimeSpan, TimeSpan> MaxSilence { get; set; } = QuartzJobsHealthChecksBuilderExtensions.DefaultMaxSilence;

    /// <summary>
    /// Whether the outbox's processing and cleanup jobs are watched too; <see langword="false"/> by default (see the
    /// remarks). Set it for a service whose only jobs are the outbox's and which would rather be restarted than alerted.
    /// </summary>
    public bool WatchOutboxJobs { get; set; }

    /// <summary>Leaves <paramref name="job"/> unwatched.</summary>
    public QuartzJobsHealthCheckOptions Exclude(JobKey job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return Exclude(key => key.Equals(job));
    }

    /// <summary>
    /// Leaves <typeparamref name="TJob"/> unwatched, by the key <c>AddIntervalJob</c> / <c>AddCronJob</c> give it
    /// (<see cref="IntervalJobSetup.JobKeyFor{TJob}"/>).
    /// </summary>
    public QuartzJobsHealthCheckOptions Exclude<TJob>()
        where TJob : IJob
        => Exclude(IntervalJobSetup.JobKeyFor<TJob>());

    /// <summary>Leaves every job matching <paramref name="predicate"/> unwatched.</summary>
    public QuartzJobsHealthCheckOptions Exclude(Func<JobKey, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _excluded.Add(predicate);
        return this;
    }

    /// <summary>Whether the check watches <paramref name="job"/>.</summary>
    internal bool Watches(JobKey job)
    {
        if (!WatchOutboxJobs && OutboxJobs.IsOutboxJob(job))
            return false;

        return !_excluded.Any(excluded => excluded(job));
    }
}
