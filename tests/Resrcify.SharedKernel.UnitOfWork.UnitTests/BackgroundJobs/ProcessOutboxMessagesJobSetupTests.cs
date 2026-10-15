using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

/// <summary>
/// Drives the Quartz 4 registration through a real scheduler built from DI.
/// </summary>
/// <remarks>
/// The job tests call <c>Execute</c> directly with a substituted context, so they never touch the part of
/// the Quartz 4 migration that actually changed: jobs are no longer added through
/// <c>IConfigureOptions&lt;QuartzOptions&gt;</c> but through the <c>AddQuartz</c> builder, and the
/// registration is only applied when the scheduler is built. These tests build that scheduler and read
/// back what it holds, so a registration that compiles but never reaches the scheduler fails here.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class ProcessOutboxMessagesJobSetupTests
{
    private static readonly JobKey OutboxJobKey = OutboxJobs.Process<TestDbContext>();

    [Fact]
    public async Task AddProcessOutboxMessagesJob_Should_RegisterTheJobOnTheScheduler()
    {
        await using var provider = BuildProvider(services => services.AddQuartz(quartz =>
            quartz.AddProcessOutboxMessagesJob<TestDbContext>()));
        var scheduler = await GetSchedulerAsync(provider);

        var job = await scheduler.GetJobDetail(OutboxJobKey);

        job.ShouldNotBeNull();
        job.JobType.ShouldBe(typeof(ProcessOutboxMessagesJob<TestDbContext>));
    }

    /// <summary>
    /// The job reads these by name, so the key names are a contract: a rename on one side only would make
    /// the job silently fall back to its defaults.
    /// </summary>
    [Fact]
    public async Task AddProcessOutboxMessagesJob_Should_PassBatchSizeAndRetryLimitAsJobData()
    {
        await using var provider = BuildProvider(services => services.AddQuartz(quartz =>
            quartz.AddProcessOutboxMessagesJob<TestDbContext>(
                processBatchSize: 7,
                retries: new RetrySchedule(5, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(20)),
                transientRetries: new RetrySchedule(9, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(1), Jitter: true))));
        var scheduler = await GetSchedulerAsync(provider);

        var job = await scheduler.GetJobDetail(OutboxJobKey);

        job.ShouldNotBeNull();
        job.JobDataMap.TryGetValue("ProcessBatchSize", out var batchSize).ShouldBeTrue();
        batchSize.ShouldBe(7);
        job.JobDataMap.TryGetValue("ProcessMaxRetryCount", out var maxRetryCount).ShouldBeTrue();
        maxRetryCount.ShouldBe(5);
        job.JobDataMap.TryGetValue("ProcessMaxTransientRetryCount", out var maxTransientRetryCount).ShouldBeTrue();
        maxTransientRetryCount.ShouldBe(9);
        job.JobDataMap["ProcessRetryDelayInMilliseconds"].ShouldBe(2_000L);
        job.JobDataMap["ProcessMaxRetryDelayInMilliseconds"].ShouldBe(20_000L);
        job.JobDataMap["ProcessRetryJitter"].ShouldBe(false);
        job.JobDataMap["ProcessTransientRetryDelayInMilliseconds"].ShouldBe(3_000L);
        job.JobDataMap["ProcessMaxTransientRetryDelayInMilliseconds"].ShouldBe(60_000L);
        job.JobDataMap["ProcessTransientRetryJitter"].ShouldBe(true);
    }

    [Fact]
    public async Task AddProcessOutboxMessagesJob_Should_ScheduleARepeatingTriggerAtTheGivenInterval()
    {
        await using var provider = BuildProvider(services => services.AddQuartz(quartz =>
            quartz.AddProcessOutboxMessagesJob<TestDbContext>(processInterval: TimeSpan.FromSeconds(45))));
        var scheduler = await GetSchedulerAsync(provider);

        var trigger = (await scheduler.GetTriggersOfJob(OutboxJobKey)).ShouldHaveSingleItem();

        var simple = trigger.ShouldBeAssignableTo<ISimpleTrigger>();
        simple.ShouldNotBeNull();
        simple.RepeatInterval.ShouldBe(TimeSpan.FromSeconds(45));
        simple.RepeatCount.ShouldBe(-1);
        var job = (await scheduler.GetJobDetail(OutboxJobKey)).ShouldNotBeNull();
        job.JobDataMap["ProcessIntervalInMilliseconds"].ShouldBe(45_000L);
        job.JobDataMap.ContainsKey("ProcessIntervalInSeconds").ShouldBeFalse();
    }

    /// <summary>
    /// The start delay must count from when the scheduler is built, as it did under Quartz 3. Quartz 4 runs
    /// AddTrigger callbacks at scheduler construction rather than at registration, which is what keeps a
    /// slow start-up (migrations, warm-up) from eating into the delay.
    /// </summary>
    [Fact]
    public async Task AddProcessOutboxMessagesJob_Should_DelayTheFirstFireByTheGivenSeconds()
    {
        await using var provider = BuildProvider(services => services.AddQuartz(quartz =>
            quartz.AddProcessOutboxMessagesJob<TestDbContext>(delayBeforeStart: TimeSpan.FromSeconds(120))));
        var before = DateTimeOffset.UtcNow;
        var scheduler = await GetSchedulerAsync(provider);
        var after = DateTimeOffset.UtcNow;

        var trigger = (await scheduler.GetTriggersOfJob(OutboxJobKey)).ShouldHaveSingleItem();

        trigger.StartTimeUtc.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(120).AddSeconds(-1));
        trigger.StartTimeUtc.ShouldBeLessThanOrEqualTo(after.AddSeconds(120).AddSeconds(1));
    }

    [Fact]
    public async Task AddProcessOutboxMessagesJob_Should_UseTheDocumentedDefaults()
    {
        await using var provider = BuildProvider(services => services.AddQuartz(quartz =>
            quartz.AddProcessOutboxMessagesJob<TestDbContext>()));
        var scheduler = await GetSchedulerAsync(provider);

        var job = await scheduler.GetJobDetail(OutboxJobKey);
        var trigger = (await scheduler.GetTriggersOfJob(OutboxJobKey)).ShouldHaveSingleItem();

        job.ShouldNotBeNull();
        job.JobDataMap.TryGetValue("ProcessBatchSize", out var batchSize).ShouldBeTrue();
        batchSize.ShouldBe(20);
        job.JobDataMap.TryGetValue("ProcessMaxRetryCount", out var maxRetryCount).ShouldBeTrue();
        maxRetryCount.ShouldBe(3);
        job.JobDataMap.TryGetValue("ProcessMaxTransientRetryCount", out var maxTransientRetryCount).ShouldBeTrue();
        maxTransientRetryCount.ShouldBe(12);
        job.JobDataMap.TryGetValue("ProcessRetryDelayInMilliseconds", out var retryDelay).ShouldBeTrue();
        retryDelay.ShouldBe(5_000L);
        job.JobDataMap.TryGetValue("ProcessMaxRetryDelayInMilliseconds", out var maxRetryDelay).ShouldBeTrue();
        maxRetryDelay.ShouldBe(300_000L);
        trigger.ShouldBeAssignableTo<ISimpleTrigger>().RepeatInterval.ShouldBe(TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// AddOutboxProcessing now calls AddQuartz itself, while every service also calls AddQuartz for its own
    /// jobs. Quartz 4 refuses a second unkeyed scheduler in some cases, so both orders are pinned: the
    /// outbox job must land on the one default scheduler, exactly once, without throwing.
    /// </summary>
    [Fact]
    public async Task AddOutboxProcessing_Should_ComposeWithTheApplicationsAddQuartz_WhenCalledAfterIt()
    {
        await using var provider = BuildProvider(services =>
        {
            services.AddQuartz();
            services.AddOutboxProcessing<TestDbContext>(new SystemTextJsonOutboxSerializer());
        });
        var scheduler = await GetSchedulerAsync(provider);

        (await scheduler.GetJobDetail(OutboxJobKey)).ShouldNotBeNull();
        (await scheduler.GetTriggersOfJob(OutboxJobKey)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task AddOutboxProcessing_Should_ComposeWithTheApplicationsAddQuartz_WhenCalledBeforeIt()
    {
        await using var provider = BuildProvider(services =>
        {
            services.AddOutboxProcessing<TestDbContext>(new SystemTextJsonOutboxSerializer());
            services.AddQuartz();
        });
        var scheduler = await GetSchedulerAsync(provider);

        (await scheduler.GetJobDetail(OutboxJobKey)).ShouldNotBeNull();
        (await scheduler.GetTriggersOfJob(OutboxJobKey)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task AddOutboxProcessing_Should_ApplyTheConfiguredOptions()
    {
        await using var provider = BuildProvider(services => services.AddOutboxProcessing<TestDbContext>(
            new SystemTextJsonOutboxSerializer(),
            options =>
            {
                options.BatchSize = 9;
                options.Retries = new RetrySchedule(4, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1));
                options.TransientRetries = options.Retries with { MaxAttempts = 20 };
                options.ProcessInterval = TimeSpan.FromSeconds(30);
            }));
        var scheduler = await GetSchedulerAsync(provider);

        var job = await scheduler.GetJobDetail(OutboxJobKey);
        var trigger = (await scheduler.GetTriggersOfJob(OutboxJobKey)).Single();

        job.ShouldNotBeNull();
        job.JobDataMap.TryGetValue("ProcessBatchSize", out var batchSize).ShouldBeTrue();
        batchSize.ShouldBe(9);
        job.JobDataMap.TryGetValue("ProcessMaxRetryCount", out var maxRetryCount).ShouldBeTrue();
        maxRetryCount.ShouldBe(4);
        job.JobDataMap.TryGetValue("ProcessMaxTransientRetryCount", out var maxTransientRetryCount).ShouldBeTrue();
        maxTransientRetryCount.ShouldBe(20);
        job.JobDataMap.TryGetValue("ProcessRetryDelayInMilliseconds", out var retryDelay).ShouldBeTrue();
        retryDelay.ShouldBe(30_000L);
        job.JobDataMap.TryGetValue("ProcessMaxRetryDelayInMilliseconds", out var maxRetryDelay).ShouldBeTrue();
        maxRetryDelay.ShouldBe(3_600_000L);
        trigger.ShouldBeAssignableTo<ISimpleTrigger>().RepeatInterval.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task AddProcessOutboxMessagesJob_Should_GiveEachDbContextItsOwnJobs_WhenTwoHaveAnOutbox()
    {
        await using var provider = BuildProvider(services => services.AddQuartz(quartz =>
        {
            quartz.AddProcessOutboxMessagesJob<TestDbContext>();
            quartz.AddProcessOutboxMessagesJob<SecondTestDbContext>();
        }));

        var scheduler = await GetSchedulerAsync(provider);

        (await scheduler.GetJobDetail(OutboxJobs.Process<TestDbContext>())).ShouldNotBeNull();
        (await scheduler.GetJobDetail(OutboxJobs.Process<SecondTestDbContext>())).ShouldNotBeNull();
        (await scheduler.GetJobDetail(OutboxJobs.Cleanup<TestDbContext>())).ShouldNotBeNull();
        (await scheduler.GetJobDetail(OutboxJobs.Cleanup<SecondTestDbContext>())).ShouldNotBeNull();
    }

    [Fact]
    public async Task AddProcessOutboxMessagesJob_Should_ScheduleTheHourlyCleanup_WhenRetentionIsOn()
    {
        await using var provider = BuildProvider(services => services.AddQuartz(quartz =>
            quartz.AddProcessOutboxMessagesJob<TestDbContext>()));
        var scheduler = await GetSchedulerAsync(provider);

        var trigger = (await scheduler.GetTriggersOfJob(OutboxJobs.Cleanup<TestDbContext>())).ShouldHaveSingleItem();

        trigger.ShouldBeAssignableTo<ISimpleTrigger>().RepeatInterval.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task AddProcessOutboxMessagesJob_Should_NotScheduleTheCleanup_WhenRetentionIsOff()
    {
        await using var provider = BuildProvider(services => services.AddQuartz(quartz =>
            quartz.AddProcessOutboxMessagesJob<TestDbContext>(processedRetention: TimeSpan.Zero)));
        var scheduler = await GetSchedulerAsync(provider);

        (await scheduler.GetJobDetail(OutboxJobs.Cleanup<TestDbContext>())).ShouldBeNull();
    }

    [Fact]
    public void AddOutboxProcessing_ShouldThrow_WithoutRetries()
        => Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddOutboxProcessing<TestDbContext>(
                configure: options => options.Retries = null!))
            .ParamName.ShouldBe(nameof(OutboxOptions.Retries));

    [Theory]
    [InlineData(5, 4)]
    [InlineData(13, 12)]
    public void AddOutboxProcessing_ShouldThrow_WhenTheTransientScheduleHasFewerAttempts(
        int maxAttempts,
        int transientMaxAttempts)
        => Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddOutboxProcessing<TestDbContext>(
            configure: options =>
            {
                options.Retries = options.Retries with { MaxAttempts = maxAttempts };
                options.TransientRetries = options.Retries with { MaxAttempts = transientMaxAttempts };
            }))
            .ParamName.ShouldBe(nameof(OutboxOptions.TransientRetries));

    [Theory]
    [InlineData(20, 20)]
    [InlineData(5, 12)]
    public async Task AddOutboxProcessing_ShouldGiveATransientFailureTheHigherOfTwelveAndMaxRetryCount_WhenItIsUnset(
        int maxRetryCount,
        int expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutboxProcessing<TestDbContext>(
            new SystemTextJsonOutboxSerializer(),
            options => options.Retries = options.Retries with { MaxAttempts = maxRetryCount });
        await using var provider = services.BuildServiceProvider();
        var scheduler = await GetSchedulerAsync(provider);

        var job = await scheduler.GetJobDetail(OutboxJobKey);

        job.ShouldNotBeNull();
        job.JobDataMap.TryGetValue("ProcessMaxRetryCount", out var stored).ShouldBeTrue();
        stored.ShouldBe(maxRetryCount);
        job.JobDataMap.TryGetValue("ProcessMaxTransientRetryCount", out var storedTransient).ShouldBeTrue();
        storedTransient.ShouldBe(expected);
        services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<OutboxBacklogSettings<TestDbContext>>()
            .Last()
            .RetryLimit.ShouldBe(expected);
    }

    [Fact]
    public async Task AddProcessOutboxMessagesJob_ShouldGiveATransientFailureMaxRetryCount_WhenItIsUnsetAndAboveTwelve()
    {
        await using var provider = BuildProvider(services => services.AddQuartz(quartz =>
            quartz.AddProcessOutboxMessagesJob<TestDbContext>(
                retries: OutboxRetryLimits.DefaultRetries with { MaxAttempts = 20 })));
        var scheduler = await GetSchedulerAsync(provider);

        var job = await scheduler.GetJobDetail(OutboxJobKey);

        job.ShouldNotBeNull();
        job.JobDataMap.TryGetValue("ProcessMaxTransientRetryCount", out var maxTransientRetryCount).ShouldBeTrue();
        maxTransientRetryCount.ShouldBe(20);
    }

    [Fact]
    public void AddProcessOutboxMessagesJob_ShouldThrow_WhenTheTransientRetryLimitIsBelowTheOther()
        => Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddQuartz(quartz =>
                quartz.AddProcessOutboxMessagesJob<TestDbContext>(
                    retries: OutboxRetryLimits.DefaultRetries with { MaxAttempts = 5 },
                    transientRetries: OutboxRetryLimits.DefaultRetries with { MaxAttempts = 4 })))
            .ParamName.ShouldBe("transientRetries");

    [Fact]
    public void AddOutboxProcessing_ShouldMeasureTheBacklogAgainstTheHigherRetryLimit()
    {
        var services = new ServiceCollection();

        services.AddOutboxProcessing<TestDbContext>(new SystemTextJsonOutboxSerializer());

        services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<OutboxBacklogSettings<TestDbContext>>()
            .ShouldHaveSingleItem()
            .RetryLimit.ShouldBe(12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddOutboxProcessing_ShouldThrow_WhenTheBatchSizeIsNotPositive(int batchSize)
        => Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddOutboxProcessing<TestDbContext>(
            new SystemTextJsonOutboxSerializer(),
            options => options.BatchSize = batchSize));

    [Fact]
    public async Task AddOutboxProcessing_ShouldScheduleTheFirstRuns_OnTheClockRegisteredBefore()
    {
        var fake = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var provider = BuildProvider(services =>
        {
            services.AddSingleton<TimeProvider>(fake);
            services.AddOutboxProcessing<TestDbContext>(new SystemTextJsonOutboxSerializer());
        });
        var scheduler = await GetSchedulerAsync(provider);

        var process = (await scheduler.GetTriggersOfJob(OutboxJobKey)).ShouldHaveSingleItem();
        var cleanup = (await scheduler.GetTriggersOfJob(OutboxJobs.Cleanup<TestDbContext>())).ShouldHaveSingleItem();

        process.StartTimeUtc.ShouldBe(fake.GetUtcNow().AddSeconds(60));
        cleanup.StartTimeUtc.ShouldBe(fake.GetUtcNow().AddMinutes(5));
    }

    [Fact]
    public void AddOutboxProcessing_ShouldGiveTheLanesTheOutboxsClaim_WhenTheyHaveNone()
    {
        var services = new ServiceCollection();

        services.AddOutboxProcessing<TestDbContext>(
            new SystemTextJsonOutboxSerializer(),
            options => options.Claim = PostgresOutboxLaneClaim.Instance);

        LaneOptions(services).Claim.ShouldBeSameAs(PostgresOutboxLaneClaim.Instance);
    }

    [Fact]
    public void AddOutboxProcessing_ShouldGiveTheLanesTheOutboxsClaim_WhenTheyWereTunedBefore()
    {
        var services = new ServiceCollection();

        services.AddOutboxLanes<TestDbContext>(lanes => lanes.MaxConcurrency = 2);
        services.AddOutboxProcessing<TestDbContext>(
            new SystemTextJsonOutboxSerializer(),
            options => options.Claim = PostgresOutboxLaneClaim.Instance);

        LaneOptions(services).Claim.ShouldBeSameAs(PostgresOutboxLaneClaim.Instance);
        LaneOptions(services).MaxConcurrency.ShouldBe(2);
    }

    [Fact]
    public void AddOutboxLanes_ShouldTakeTheOutboxsClaim_WhenTunedAfterIt()
    {
        var services = new ServiceCollection();

        services.AddOutboxProcessing<TestDbContext>(
            new SystemTextJsonOutboxSerializer(),
            options => options.Claim = PostgresOutboxLaneClaim.Instance);
        services.AddOutboxLanes<TestDbContext>(lanes => lanes.MaxConcurrency = 2);

        LaneOptions(services).Claim.ShouldBeSameAs(PostgresOutboxLaneClaim.Instance);
    }

    private static OutboxLaneOptions LaneOptions(IServiceCollection services)
        => services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<OutboxLaneSettings<TestDbContext>>()
            .Last()
            .Options;

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static async Task<IScheduler> GetSchedulerAsync(IServiceProvider provider)
        => await provider
            .GetRequiredService<ISchedulerFactory>()
            .GetScheduler();
}
