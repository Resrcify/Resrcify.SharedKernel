using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;
using static Resrcify.SharedKernel.UnitOfWork.UnitTests.Models.OutboxBacklogTestHost;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxBacklogMonitorTests
{
    [Fact]
    public async Task MeasureOnceAsync_ShouldCountWaitingAndGivenUpMessages_AndAgeTheOldestWaiting()
    {
        await using var host = await CreateAsync();
        await host.SeedAsync(
            Message(occurredAgo: TimeSpan.FromMinutes(10)),
            Message(occurredAgo: TimeSpan.FromMinutes(1), retryCount: 2),
            Message(occurredAgo: TimeSpan.FromHours(1), retryCount: 3),   // gave up
            Message(occurredAgo: TimeSpan.FromHours(2), processed: true));

        await host.Monitor.MeasureOnceAsync(CancellationToken.None);

        var backlog = host.Monitor.Latest.ShouldNotBeNull();
        backlog.Waiting.ShouldBe(2);
        backlog.Poison.ShouldBe(1);
        backlog.OldestWaitingAge.ShouldBe(TimeSpan.FromMinutes(10));
        backlog.MeasuredAt.ShouldBe(Start);
    }

    [Fact]
    public async Task MeasureOnceAsync_ShouldCountAMessageMarkedGivenUp_AsGivenUpNotWaiting()
    {
        await using var host = await CreateAsync();
        var givenUp = Message(occurredAgo: TimeSpan.FromHours(1), retryCount: 3);
        givenUp.ProcessedOnUtc = OutboxMessage.GivenUpProcessedOnUtc;
        await host.SeedAsync(
            givenUp,
            Message(occurredAgo: TimeSpan.FromMinutes(5)),
            Message(occurredAgo: TimeSpan.FromHours(2), processed: true));

        await host.Monitor.MeasureOnceAsync(CancellationToken.None);

        var backlog = host.Monitor.Latest.ShouldNotBeNull();
        backlog.Waiting.ShouldBe(1);
        backlog.Poison.ShouldBe(1);
        backlog.OldestWaitingAge.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task MeasureOnceAsync_ShouldFail_WhenTheOutboxTableIsNotMigrated()
    {
        // Every lane poll fails on such a table; the measurement failing too is what turns the health check unhealthy.
        await using var host = await OutboxBacklogTestHost.CreateAsync();
        await host.SeedAsync(OutboxBacklogTestHost.Message(TimeSpan.FromMinutes(1)));
        await host.DropNextAttemptColumnAsync();

        await host.Monitor.MeasureOnceAsync(CancellationToken.None);

        host.Monitor.Latest.ShouldBeNull();
    }

    [Fact]
    public async Task MeasureOnceAsync_ShouldCountTheMessagesALaneTriesLater()
    {
        await using var host = await OutboxBacklogTestHost.CreateAsync();
        var later = OutboxBacklogTestHost.Message(TimeSpan.FromMinutes(1), retryCount: 1);
        later.NextAttemptOnUtc = OutboxBacklogTestHost.Start.UtcDateTime.AddSeconds(30);
        await host.SeedAsync(later, OutboxBacklogTestHost.Message(TimeSpan.FromMinutes(2)));

        await host.Monitor.MeasureOnceAsync(CancellationToken.None);

        host.Monitor.Latest.ShouldNotBeNull().Waiting.ShouldBe(2);
        host.Monitor.Latest.WaitingForLaterTry.ShouldBe(1);
    }

    [Fact]
    public async Task MeasureOnceAsync_ShouldReportNoBacklog_WhenEverythingIsProcessed()
    {
        await using var host = await CreateAsync();
        await host.SeedAsync(Message(occurredAgo: TimeSpan.FromHours(1), processed: true));

        await host.Monitor.MeasureOnceAsync(CancellationToken.None);

        var backlog = host.Monitor.Latest.ShouldNotBeNull();
        backlog.Waiting.ShouldBe(0);
        backlog.Poison.ShouldBe(0);
        backlog.OldestWaitingAge.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task MeasureOnceAsync_ShouldCountALaneMessageAsWaiting_WhileItsLaneStillTriesIt()
    {
        // The lane allows 5 tries: its message with 3 failures still waits; a regular one with 3 gave up.
        await using var host = await CreateAsync(laneEvent: typeof(LaneEvent), laneMaxRetryCount: 5);
        await host.SeedAsync(
            Message(occurredAgo: TimeSpan.FromMinutes(2), retryCount: 3, type: typeof(LaneEvent).FullName!),
            Message(occurredAgo: TimeSpan.FromMinutes(2), retryCount: 3));

        await host.Monitor.MeasureOnceAsync(CancellationToken.None);

        var backlog = host.Monitor.Latest.ShouldNotBeNull();
        backlog.Waiting.ShouldBe(1);
        backlog.Poison.ShouldBe(1);
    }

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Only its type name is used: it stands for an event in a lane.")]
    private sealed class LaneEvent;
}
