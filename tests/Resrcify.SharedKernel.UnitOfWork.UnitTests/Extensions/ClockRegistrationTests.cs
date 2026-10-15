using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Extensions;

/// <summary>
/// The outbox and the jobs read the container's <see cref="TimeProvider"/>, whenever it is registered: a fake one
/// registered after them (as <c>ConfigureTestServices</c> does) is the one used, not a clock captured at registration.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ClockRegistrationTests
{
    private static readonly DateTimeOffset Start = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheJobsFirstRuns_ShouldBeOnAFakeClock_RegisteredAfterTheOutboxAndTheJobs()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutboxProcessing<TestDbContext>(outbox => outbox.DelayBeforeStart = TimeSpan.FromSeconds(60));
        services.AddQuartz(quartz => quartz
            .AddIntervalJob<NoopJob>(TimeSpan.FromMinutes(5), startAfter: TimeSpan.FromMinutes(1))
            .AddCronJob<GenericJob<string>>("0 0 16 * * ?"));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));

        await using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await StartOf(scheduler, OutboxJobs.Process<TestDbContext>())).ShouldBe(Start.AddSeconds(60));
        (await StartOf(scheduler, OutboxJobs.Cleanup<TestDbContext>())).ShouldBe(Start.AddMinutes(5));
        (await StartOf(scheduler, IntervalJobSetup.JobKeyFor<NoopJob>())).ShouldBe(Start.AddMinutes(1));
        (await StartOf(scheduler, IntervalJobSetup.JobKeyFor<GenericJob<string>>())).ShouldBe(Start);
    }

    [Fact]
    public async Task TheOutboxTypes_ShouldBeBuiltWithAFakeClock_RegisteredAfterTheirRegistrations()
    {
        var clock = new FakeTimeProvider(Start);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutboxProcessing<TestDbContext>();
        services.AddEntityInterceptors().AddOutboxInterceptor();
        services.AddQuartz(quartz => quartz.AddJobHeartbeats());
        services.AddSingleton<TimeProvider>(clock);

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(clock);
        provider.GetRequiredService<JobHeartbeatRegistry>().StartedAt.ShouldBe(Start);
        Should.NotThrow(() => provider.GetRequiredService<UpdateAuditableEntitiesInterceptor>());
        Should.NotThrow(() => ActivatorUtilities.CreateInstance<ProcessOutboxMessagesJob<TestDbContext>>(provider));
    }

    [Fact]
    public async Task TheSystemClock_ShouldBeRegistered_WhenNoneIs()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutboxProcessing<TestDbContext>();

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
    }

    private static async Task<DateTimeOffset> StartOf(
        IScheduler scheduler,
        JobKey job)
        => (await scheduler.GetTriggersOfJob(job)).Single().StartTimeUtc;
}
