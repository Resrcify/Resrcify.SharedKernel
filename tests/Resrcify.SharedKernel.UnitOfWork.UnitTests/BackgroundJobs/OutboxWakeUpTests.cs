using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
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
public sealed class OutboxWakeUpTests
{
    private static readonly JobKey Job = OutboxJobs.Process<TestDbContext>();

    private readonly IScheduler _scheduler = Substitute.For<IScheduler>();

    public OutboxWakeUpTests()
    {
        _scheduler.Status.Returns(SchedulerStatus.Running);
        _scheduler.Exists(Job, Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task WakeAsync_ShouldRunTheJobOnce_ForWakeUpsBeforeTheRunStarts()
    {
        var wakeUp = Create();

        await wakeUp.WakeAsync();
        await wakeUp.WakeAsync();
        await wakeUp.WakeAsync();

        await _scheduler.Received(1).TriggerJob(Job, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WakeAsync_ShouldRunTheJobAgain_ForAWakeUpAfterTheRunStarted()
    {
        var wakeUp = Create();

        await wakeUp.WakeAsync();
        wakeUp.RunStarted();
        await wakeUp.WakeAsync();

        await _scheduler.Received(2).TriggerJob(Job, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WakeAsync_ShouldBeMarkedStarted_ByTheJobsRun()
    {
        var factory = Factory();
        await using var harness = await OutboxJobTestHarness.CreateAsync(
            new FakeTimeProvider(),
            services => services
                .AddLogging()
                .AddSingleton(factory)
                .AddSingleton<OutboxWakeUp<TestDbContext>>());
        var wakeUp = harness.Services.GetRequiredService<OutboxWakeUp<TestDbContext>>();

        await wakeUp.WakeAsync();
        await harness.Job.Execute(OutboxJobTestHarness.JobContext());
        await wakeUp.WakeAsync();

        await _scheduler.Received(2).TriggerJob(Job, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WakeAsync_ShouldDoNothing_WhenTheJobIsNotScheduled_AndTryAgainNextTime()
    {
        _scheduler.Exists(Job, Arg.Any<CancellationToken>()).Returns(false, true);
        var wakeUp = Create();

        await wakeUp.WakeAsync();
        await wakeUp.WakeAsync();

        await _scheduler.Received(1).TriggerJob(Job, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WakeAsync_ShouldNotThrow_WhenTheSchedulerFails_AndTryAgainNextTime()
    {
        _scheduler.Exists(Job, Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new SchedulerException("shutting down"),
                _ => true);
        var wakeUp = Create();

        await wakeUp.WakeAsync();
        await wakeUp.WakeAsync();

        await _scheduler.Received(1).TriggerJob(Job, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WakeAsync_ShouldOnlyWakeTheLanes_WithoutAScheduler()
    {
        var wakeUp = new OutboxWakeUp<TestDbContext>(
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<OutboxWakeUp<TestDbContext>>.Instance);
        var lane = new WakeSignal();
        using var registration = wakeUp.WakeLane(lane);

        await wakeUp.WakeAsync();

        var woken = lane.WaitAsync(TimeSpan.FromMinutes(1), new FakeTimeProvider(), CancellationToken.None);
        await woken.WaitAsync(TimeSpan.FromSeconds(5));
        woken.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task WakeAsync_ShouldStopWakingALane_ThatLeft()
    {
        var wakeUp = Create();
        var lane = new WakeSignal();
        wakeUp.WakeLane(lane).Dispose();

        await wakeUp.WakeAsync();

        var waiting = lane.WaitAsync(TimeSpan.FromMinutes(1), new FakeTimeProvider(), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        waiting.IsCompleted.ShouldBeFalse();
    }

    private OutboxWakeUp<TestDbContext> Create()
        => new(
            new ServiceCollection().AddSingleton(Factory()).BuildServiceProvider(),
            NullLogger<OutboxWakeUp<TestDbContext>>.Instance);

    private ISchedulerFactory Factory()
    {
        var factory = Substitute.For<ISchedulerFactory>();
        factory.GetScheduler(Arg.Any<CancellationToken>()).Returns(_scheduler);
        return factory;
    }
}
