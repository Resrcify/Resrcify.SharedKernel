using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

/// <summary>
/// The liveness check on a scheduler that is built but not started, with time moved by hand: whether a job "fired" is
/// what its heartbeats say.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class QuartzJobsHealthCheckTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly JobKey Ranks = IntervalJobSetup.JobKeyFor<NoopJob>();

    [Fact]
    public async Task CheckHealthAsync_ShouldBeHealthy_BeforeAJobIsDue()
    {
        await using var host = await BuildAsync(quartz => quartz.AddIntervalJob<NoopJob>(TimeSpan.FromMinutes(1), startAfter: TimeSpan.FromMinutes(10)));

        // 9 minutes in: past three intervals and two minutes since start, but the job isn't due yet.
        host.Clock.Advance(TimeSpan.FromMinutes(9));
        var result = await host.CheckAsync();

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldFail_WhenAJobStopsFiring()
    {
        await using var host = await BuildAsync(quartz => quartz.AddIntervalJob<NoopJob>(TimeSpan.FromMinutes(1)));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Registry.Beat(Ranks);

        // The default limit for a 1-minute job is 3 intervals + 2 minutes = 5 minutes.
        host.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var result = await host.CheckAsync();

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldNotBeNull().ShouldContain(Ranks.ToString());
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldBeHealthy_WhileAJobBeatsWithinItsLimit()
    {
        await using var host = await BuildAsync(quartz => quartz.AddIntervalJob<NoopJob>(TimeSpan.FromMinutes(1)));
        host.Clock.Advance(TimeSpan.FromMinutes(30));
        host.Registry.Beat(Ranks);

        host.Clock.Advance(TimeSpan.FromMinutes(4));
        var result = await host.CheckAsync();

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data[Ranks.ToString()].ShouldBe(240d);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReadACronJobsInterval_FromItsSchedule()
    {
        var job = new JobKey("payouts");
        await using var host = await BuildAsync(quartz => quartz
            .AddJob<NoopJob>(configure => configure.WithIdentity(job))
            .AddTrigger(trigger => trigger.ForJob(job).StartAt(Start).WithCronSchedule("0 0/15 * * * ?")));
        host.Registry.Beat(job);

        host.Clock.Advance(TimeSpan.FromMinutes(46));
        (await host.CheckAsync()).Status.ShouldBe(HealthStatus.Healthy);

        host.Clock.Advance(TimeSpan.FromMinutes(2));
        (await host.CheckAsync()).Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldWaitForTheNextDueFire_WhenTheScheduleIsUneven()
    {
        // Every 5 minutes from 09:00 to 17:55 (UTC): after 17:55 the next fire is 09:00 the next day, not 18:00.
        var job = new JobKey("business-hours");
        await using var host = await BuildAsync(quartz => quartz
            .AddJob<NoopJob>(configure => configure.WithIdentity(job))
            .AddTrigger(trigger => trigger
                .ForJob(job)
                .StartAt(Start)
                .WithCronSchedule("0 0/5 9-17 ? * *", cron => cron.InTimeZone(TimeZoneInfo.Utc))));
        host.Clock.Advance(new TimeSpan(5, 55, 0));   // 17:55
        host.Registry.Beat(job);

        host.Clock.Advance(TimeSpan.FromMinutes(18));   // 18:13: silent 18 minutes overnight, as scheduled
        (await host.CheckAsync()).Status.ShouldBe(HealthStatus.Healthy);

        host.Clock.Advance(new TimeSpan(14, 59, 0));   // 09:12 the next day: due 12 minutes ago, within its limit
        (await host.CheckAsync()).Status.ShouldBe(HealthStatus.Healthy);

        host.Clock.Advance(TimeSpan.FromMinutes(2));   // 09:14: it should have fired twice more by now
        (await host.CheckAsync()).Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldSkipAPausedJob()
    {
        await using var host = await BuildAsync(quartz => quartz.AddIntervalJob<NoopJob>(TimeSpan.FromMinutes(1)));
        var scheduler = await host.SchedulerAsync();
        await scheduler.PauseJob(Ranks);

        host.Clock.Advance(TimeSpan.FromHours(1));
        var result = await host.CheckAsync();

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data.ShouldBeEmpty();
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldUseTheGivenLimit()
    {
        await using var host = await BuildAsync(
            quartz => quartz.AddIntervalJob<NoopJob>(TimeSpan.FromMinutes(1)),
            maxSilence: interval => interval * 30);
        host.Registry.Beat(Ranks);

        host.Clock.Advance(TimeSpan.FromMinutes(20));

        (await host.CheckAsync()).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task AddQuartzJobs_ShouldSayWhatIsMissing_WithoutHeartbeats()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz();
        services.AddHealthChecks().AddQuartzJobs(tags: ["live"]);
        await using var provider = services.BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single();

        registration.Tags.ShouldContain("live");
        registration.FailureStatus.ShouldBe(HealthStatus.Unhealthy);
        Should.Throw<InvalidOperationException>(() => registration.Factory(provider)).Message.ShouldContain("AddJobHeartbeats");
    }

    private static async Task<TestHost> BuildAsync(
        Action<IQuartzBuilder> configure,
        Func<TimeSpan, TimeSpan>? maxSilence = null)
    {
        var clock = new FakeTimeProvider(Start);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddQuartz(quartz =>
        {
            quartz.AddJobHeartbeats();
            configure(quartz);
        });
        services.AddHealthChecks().AddQuartzJobs(maxSilence);
        var provider = services.BuildServiceProvider();
        // Made now, as the listener makes it when the scheduler starts: its start is the process start. The scheduler
        // too, as the host builds it at start-up: its triggers' start times are set then.
        provider.GetRequiredService<JobHeartbeatRegistry>();
        await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        return new TestHost(provider, clock);
    }

    private sealed class TestHost(
        ServiceProvider provider,
        FakeTimeProvider clock)
        : IAsyncDisposable
    {
        public FakeTimeProvider Clock => clock;

        public JobHeartbeatRegistry Registry => provider.GetRequiredService<JobHeartbeatRegistry>();

        public Task<IScheduler> SchedulerAsync()
            => provider.GetRequiredService<ISchedulerFactory>().GetScheduler().AsTask();

        public Task<HealthCheckResult> CheckAsync()
        {
            var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single();
            return registration.Factory(provider).CheckHealthAsync(new HealthCheckContext { Registration = registration });
        }

        public ValueTask DisposeAsync()
            => provider.DisposeAsync();
    }
}
