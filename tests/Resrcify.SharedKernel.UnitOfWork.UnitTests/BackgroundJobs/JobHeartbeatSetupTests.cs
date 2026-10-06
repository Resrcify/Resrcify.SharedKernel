using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class JobHeartbeatSetupTests
{
    [Fact]
    public async Task AddJobHeartbeats_ShouldBeatForAJobThatRuns_WithoutCodeInTheJob()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(quartz => quartz
            .AddJobHeartbeats()
            .AddJob<NoopJob>(job => job.WithIdentity(nameof(NoopJob)).StoreDurably()));
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<JobHeartbeatRegistry>();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        await scheduler.TriggerJob(new JobKey(nameof(NoopJob)), data: null);

        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(10);
        while (registry.LastBeat(new JobKey(nameof(NoopJob))) is null && TimeProvider.System.GetUtcNow() < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        await scheduler.Shutdown(waitForJobsToComplete: true);
        registry.LastBeat(new JobKey(nameof(NoopJob))).ShouldNotBeNull();
    }

    [Fact]
    public void AddJobHeartbeats_ShouldRegisterOnce_WhenCalledTwice()
    {
        var services = new ServiceCollection();

        services.AddQuartz(quartz => quartz.AddJobHeartbeats().AddJobHeartbeats());

        services.Count(service => service.ServiceType == typeof(JobHeartbeatRegistry)).ShouldBe(1);
    }
}
