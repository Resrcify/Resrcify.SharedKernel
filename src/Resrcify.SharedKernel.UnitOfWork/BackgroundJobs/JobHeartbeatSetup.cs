using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>Records a heartbeat for every Quartz job, for the <c>AddQuartzJobs</c> liveness check.</summary>
public static class JobHeartbeatSetup
{
    /// <summary>
    /// Stamps a heartbeat in <see cref="JobHeartbeatRegistry"/> every time a job of this scheduler fires or finishes:
    /// one listener for every job, no code in the jobs. Pair with
    /// <c>services.AddHealthChecks().AddQuartzJobs(tags: ["live"])</c>.
    /// </summary>
    public static IQuartzBuilder AddJobHeartbeats(this IQuartzBuilder quartz)
    {
        ArgumentNullException.ThrowIfNull(quartz);
        if (quartz.Services.Any(service => service.ServiceType == typeof(JobHeartbeatRegistry)))
            return quartz;

        quartz.Services.TryAddSingleton(TimeProvider.System);
        quartz.Services.TryAddSingleton<JobHeartbeatRegistry>();
        return quartz.AddJobListener<JobHeartbeatListener>(GroupMatcher<JobKey>.AnyGroup());
    }
}

/// <summary>Stamps the job's heartbeat when it is about to run and when it has run (whatever the outcome).</summary>
internal sealed class JobHeartbeatListener(JobHeartbeatRegistry registry)
    : IJobListener
{
    public string Name => "Resrcify.SharedKernel.JobHeartbeats";

    public ValueTask JobToBeExecuted(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        registry.Beat(context.JobDetail.Key);
        return ValueTask.CompletedTask;
    }

    public ValueTask JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        registry.Beat(context.JobDetail.Key);
        return ValueTask.CompletedTask;
    }
}
