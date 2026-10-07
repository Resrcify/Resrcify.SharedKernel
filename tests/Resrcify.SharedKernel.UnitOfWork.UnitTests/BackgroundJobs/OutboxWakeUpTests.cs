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
using Resrcify.SharedKernel.UnitOfWork.Outbox;
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

    [Fact]
    public async Task DrainAsync_ShouldReturn_OnceTheJobProcessedEveryMessage()
    {
        await using var harness = await DrainHarnessAsync(withScheduler: true);
        await harness.SeedAsync(Message(), Message(), Message());

        await harness.Services.GetRequiredService<OutboxWakeUp<TestDbContext>>().DrainAsync(TimeSpan.FromSeconds(10));

        (await harness.GetMessagesAsync()).ShouldAllBe(message => message.ProcessedOnUtc != null);
    }

    [Fact]
    public async Task DrainAsync_ShouldNotWaitForAMessage_WhoseNextTryIsLater()
    {
        // Nothing processes this outbox: only a message that isn't due lets the drain return.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        await using var harness = await DrainHarnessAsync(withScheduler: false, clock);
        var later = Message();
        later.NextAttemptOnUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(1);
        await harness.SeedAsync(later);

        await Should.NotThrowAsync(
            () => harness.Services.GetRequiredService<OutboxWakeUp<TestDbContext>>().DrainAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task DrainAsync_ShouldThrow_WhenNothingProcessesTheOutbox()
    {
        await using var harness = await DrainHarnessAsync(withScheduler: false);
        await harness.SeedAsync(Message());

        var timeout = await Should.ThrowAsync<TimeoutException>(
            () => harness.Services.GetRequiredService<OutboxWakeUp<TestDbContext>>().DrainAsync(TimeSpan.FromMilliseconds(200)));

        timeout.Message.ShouldContain("1 TestDbContext outbox message(s) were still due");
    }

    // The scheduler, when there is one, runs the real job on each trigger.
    private async Task<OutboxJobTestHarness> DrainHarnessAsync(bool withScheduler, FakeTimeProvider? clock = null)
    {
        clock ??= new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        var harness = await OutboxJobTestHarness.CreateAsync(
            clock,
            services =>
            {
                services.AddLogging().AddSingleton<TimeProvider>(clock).AddSingleton<OutboxWakeUp<TestDbContext>>();
                if (withScheduler)
                    services.AddSingleton(Factory());
            });
        // The run is awaited inside the trigger, so runs never overlap on the test's single SQLite connection.
        _scheduler
            .When(async scheduler => await scheduler.TriggerJob(Job, null, Arg.Any<CancellationToken>()))
            .Do(_ => harness.Job.Execute(OutboxJobTestHarness.JobContext()).AsTask().GetAwaiter().GetResult());
        return harness;
    }

    private static OutboxMessage Message()
    {
        var domainEvent = new TestDomainEvent(Guid.NewGuid(), "drained");
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(TestDomainEvent).FullName!,
            Content = new SystemTextJsonOutboxSerializer().Serialize(domainEvent),
            OccurredOnUtc = new DateTime(2026, 10, 7, 11, 0, 0, DateTimeKind.Utc),
        };
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
