using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
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
    private static readonly JobKey OutboxJobKey = new("ProcessOutboxMessagesJob");

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
                processMaxRetryCount: 5)));
        var scheduler = await GetSchedulerAsync(provider);

        var job = await scheduler.GetJobDetail(OutboxJobKey);

        job.ShouldNotBeNull();
        job.JobDataMap.TryGetValue("ProcessBatchSize", out var batchSize).ShouldBeTrue();
        batchSize.ShouldBe(7);
        job.JobDataMap.TryGetValue("ProcessMaxRetryCount", out var maxRetryCount).ShouldBeTrue();
        maxRetryCount.ShouldBe(5);
    }

    [Fact]
    public async Task AddProcessOutboxMessagesJob_Should_ScheduleARepeatingTriggerAtTheGivenInterval()
    {
        await using var provider = BuildProvider(services => services.AddQuartz(quartz =>
            quartz.AddProcessOutboxMessagesJob<TestDbContext>(processIntervalInSeconds: 45)));
        var scheduler = await GetSchedulerAsync(provider);

        var trigger = (await scheduler.GetTriggersOfJob(OutboxJobKey)).ShouldHaveSingleItem();

        var simple = trigger.ShouldBeAssignableTo<ISimpleTrigger>();
        simple.ShouldNotBeNull();
        simple.RepeatInterval.ShouldBe(TimeSpan.FromSeconds(45));
        simple.RepeatCount.ShouldBe(-1);
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
            quartz.AddProcessOutboxMessagesJob<TestDbContext>(delayInSecondsBeforeStart: 120)));
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
                options.MaxRetryCount = 4;
                options.ProcessIntervalInSeconds = 30;
            }));
        var scheduler = await GetSchedulerAsync(provider);

        var job = await scheduler.GetJobDetail(OutboxJobKey);
        var trigger = (await scheduler.GetTriggersOfJob(OutboxJobKey)).Single();

        job.ShouldNotBeNull();
        job.JobDataMap.TryGetValue("ProcessBatchSize", out var batchSize).ShouldBeTrue();
        batchSize.ShouldBe(9);
        job.JobDataMap.TryGetValue("ProcessMaxRetryCount", out var maxRetryCount).ShouldBeTrue();
        maxRetryCount.ShouldBe(4);
        trigger.ShouldBeAssignableTo<ISimpleTrigger>().RepeatInterval.ShouldBe(TimeSpan.FromSeconds(30));
    }

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
