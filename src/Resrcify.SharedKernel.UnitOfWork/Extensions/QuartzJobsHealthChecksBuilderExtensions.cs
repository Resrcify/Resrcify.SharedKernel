using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.Abstractions.Health;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Health;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>A liveness check for the Quartz jobs.</summary>
public static class QuartzJobsHealthChecksBuilderExtensions
{
    private const string MissingHeartbeats =
        "The Quartz jobs health check reads the jobs' heartbeats, which nothing records: call AddJobHeartbeats() on the "
        + "Quartz builder (services.AddQuartz(quartz => quartz.AddJobHeartbeats())).";

    /// <summary>
    /// How long a job may go without firing, by default: three of its intervals plus two minutes (a 1-minute job 5
    /// minutes, a 15-minute job 47, a 6-hour job 18 hours).
    /// </summary>
    public static readonly Func<TimeSpan, TimeSpan> DefaultMaxSilence = interval => interval * 3 + TimeSpan.FromMinutes(2);

    /// <summary>
    /// Fails (<see cref="HealthStatus.Unhealthy"/> by default) when a scheduled job hasn't fired or finished for longer
    /// than <paramref name="maxSilence"/> of its interval: the scheduler stopped firing, which a restart fixes, so it is
    /// a liveness check (tagged <see cref="HealthTags.Live"/> unless <paramref name="tags"/> says otherwise). A job
    /// that fires and fails isn't stalled. A run counts as silence until it ends, so give long-running jobs room
    /// (<c>maxSilence: interval => interval * 3 + TimeSpan.FromMinutes(30)</c>). The outbox's jobs aren't watched (see
    /// <see cref="QuartzJobsHealthCheckOptions"/>); to exclude other jobs, or watch the outbox's, use
    /// <see cref="AddQuartzJobs(IHealthChecksBuilder, Action{QuartzJobsHealthCheckOptions}, string, HealthStatus?, IEnumerable{string}?)"/>.
    /// Needs <c>AddJobHeartbeats()</c> on the Quartz builder: without it the host fails to start.
    /// </summary>
    /// <param name="tags">The check's tags: <see cref="HealthTags.Live"/> when <see langword="null"/>; <c>[]</c> for none.</param>
    /// <example><c>services.AddHealthChecks().AddQuartzJobs();</c></example>
    public static IHealthChecksBuilder AddQuartzJobs(
        this IHealthChecksBuilder builder,
        Func<TimeSpan, TimeSpan>? maxSilence = null,
        string name = "quartz-jobs",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
        => builder.AddQuartzJobs(
            (QuartzJobsHealthCheckOptions jobs) => jobs.MaxSilence = maxSilence ?? DefaultMaxSilence,
            name,
            failureStatus,
            tags);

    /// <summary>
    /// The liveness check of
    /// <see cref="AddQuartzJobs(IHealthChecksBuilder, Func{TimeSpan, TimeSpan}?, string, HealthStatus?, IEnumerable{string}?)"/>,
    /// with <paramref name="configure"/> choosing which jobs it watches and how long each may stay silent: exclude a job
    /// by key, by type or by predicate, watch the outbox's jobs (left out by default), set <c>MaxSilence</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddHealthChecks().AddQuartzJobs(
    ///     jobs => jobs.Exclude&lt;ImportArchiveJob&gt;());
    /// </code>
    /// </example>
    public static IHealthChecksBuilder AddQuartzJobs(
        this IHealthChecksBuilder builder,
        Action<QuartzJobsHealthCheckOptions> configure,
        string name = "quartz-jobs",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var options = new QuartzJobsHealthCheckOptions();
        configure(options);
        if (options.MaxSilence is null)
            throw new ArgumentException("MaxSilence can't be null; exclude a job instead of giving it no window.", nameof(configure));

        // Fails the host's start (before any hosted service starts) instead of every check run.
        builder.Services
            .AddOptions<QuartzJobsHealthCheckRequirement>()
            .Validate<IServiceProviderIsService>(
                (_, services) => services.IsService(typeof(JobHeartbeatRegistry)),
                MissingHeartbeats)
            .ValidateOnStart();

        return builder.Add(new HealthCheckRegistration(
            name,
            provider => Create(provider, options),
            failureStatus,
            tags ?? [HealthTags.Live]));
    }

    private static IHealthCheck Create(
        IServiceProvider provider,
        QuartzJobsHealthCheckOptions options)
    {
        if (provider.GetService<JobHeartbeatRegistry>() is not { } registry)
            return new MisconfiguredHealthCheck(MissingHeartbeats);

        return new QuartzJobsHealthCheck(
            registry,
            provider.GetRequiredService<ISchedulerFactory>(),
            options,
            provider.GetRequiredService<TimeProvider>());
    }
}

/// <summary>Marks that a Quartz jobs health check is registered, for start-up validation.</summary>
internal sealed class QuartzJobsHealthCheckRequirement
{
    /// <summary>What the check needs registered.</summary>
    public Type Needs { get; } = typeof(JobHeartbeatRegistry);
}
