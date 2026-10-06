using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Quartz;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class ProcessOutboxMessagesJobTests
{
    private static readonly SystemTextJsonOutboxSerializer _serializer = new();

    [Fact]
    public async Task Execute_ShouldPublishEvents_AndMarkMessagesProcessed()
    {
        // Arrange
        await using var harness = await OutboxJobTestHarness.CreateAsync();
        var messages = Enumerable
            .Range(0, 2)
            .Select(_ => CreateMessage(new TestDomainEvent(Guid.NewGuid(), "Test message")))
            .ToArray();
        await harness.SeedAsync(messages);

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 10));

        // Assert
        await harness.Publisher
            .Received(messages.Length)
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());

        var rows = await harness.GetMessagesAsync();
        rows.Count.ShouldBe(messages.Length);
        rows.ShouldAllBe(m => m.ProcessedOnUtc != null);
        rows.ShouldAllBe(m => m.Error == null);
    }

    [Fact]
    public async Task Execute_ShouldRecordError_AndIncrementRetryCount_WhenPublishThrows()
    {
        // Arrange
        await using var harness = await OutboxJobTestHarness.CreateAsync();
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("handler failed")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "boom")));

        // Act — a throwing handler must not abort the job.
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 10));

        // Assert — the row is left unprocessed (so it retries) with the error recorded.
        var row = (await harness.GetMessagesAsync()).Single();
        row.ProcessedOnUtc.ShouldBeNull();
        row.RetryCount.ShouldBe(1);
        row.Error.ShouldNotBeNull();
        row.Error.ShouldContain("handler failed");
    }

    [Fact]
    public async Task Execute_ShouldStopRetrying_OnceMaxRetryCountReached()
    {
        // Arrange
        await using var harness = await OutboxJobTestHarness.CreateAsync();
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("always fails")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "poison")));

        // Act — run more cycles than the retry budget allows.
        const int maxRetryCount = 3;
        for (var cycle = 0; cycle < maxRetryCount + 2; cycle++)
            await harness.Job.Execute(
                OutboxJobTestHarness.JobContext(batchSize: 10, maxRetryCount: maxRetryCount));

        // Assert — the message is attempted exactly maxRetryCount times, then marked given up and skipped.
        await harness.Publisher
            .Received(maxRetryCount)
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());

        var row = (await harness.GetMessagesAsync()).Single();
        row.RetryCount.ShouldBe(maxRetryCount);
        row.ProcessedOnUtc.ShouldBe(OutboxMessage.GivenUpProcessedOnUtc);
        row.Error.ShouldNotBeNull();
        row.Error.ShouldStartWith("Gave up at ");
        row.Error.ShouldContain("always fails");
    }

    [Fact]
    public async Task Execute_ShouldNotTryAGivenUpMessageAgain_WhenTheRetryLimitIsRaised()
    {
        // Arrange — the message gives up after one try.
        await using var harness = await OutboxJobTestHarness.CreateAsync();
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("always fails")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "poison")));
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 10, maxRetryCount: 1));

        // Act — a limit it is under: an unmarked message would be tried again.
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 10, maxRetryCount: 5));

        // Assert
        await harness.Publisher
            .Received(1)
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
        (await harness.GetMessagesAsync()).Single().ProcessedOnUtc.ShouldBe(OutboxMessage.GivenUpProcessedOnUtc);
    }

    /// <summary>
    /// Quartz 4 removed the typed JobDataMap getters, so the job now reads the stored object itself. These
    /// pin how it treats each shape a batch size can arrive in: honoured when it is a usable number however
    /// it was stored, and the default of 20 otherwise - never an exception that fails the job. A run drains the
    /// outbox batch by batch, so the batch size shows in how many reads the 5 messages took.
    /// </summary>
    [Theory]
    [MemberData(nameof(BatchSizeShapes))]
    public async Task Execute_ShouldReadBatchesOfTheStoredSize_HoweverItWasStored(object? storedBatchSize, int expectedReads)
    {
        // Arrange
        await using var harness = await OutboxJobTestHarness.CreateAsync();
        await harness.SeedAsync(Enumerable
            .Range(0, 5)
            .Select(_ => CreateMessage(new TestDomainEvent(Guid.NewGuid(), "Test message")))
            .ToArray());
        var data = new JobDataMap();
        if (storedBatchSize is not null)
            data.Add("ProcessBatchSize", storedBatchSize);
        var readsBefore = harness.Queries.Selects;

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(data));

        // Assert
        (harness.Queries.Selects - readsBefore).ShouldBe(expectedReads);
        (await harness.GetMessagesAsync()).ShouldAllBe(m => m.ProcessedOnUtc != null);
    }

    public static TheoryData<object?, int> BatchSizeShapes => new()
    {
        { 2, 3 },               // stored as an int, as the setup does: batches of 2, 2 and 1
        { 2L, 3 },              // a long, as a persistent store may hand it back
        { "2", 3 },             // a string, as configuration supplies it
        { "not-a-number", 1 },  // unusable: falls back to the default batch of 20
        { long.MaxValue, 1 },   // out of int range: falls back rather than overflowing
        { null, 1 },            // absent: the default batch of 20
    };

    [Fact]
    public async Task Execute_ShouldDrainTheBacklog_WhileItsBatchesComeBackFull()
    {
        // Arrange — 70 waiting, 20 a batch: four batches (20, 20, 20, 10) in one run.
        await using var harness = await OutboxJobTestHarness.CreateAsync(new FakeTimeProvider());
        await harness.SeedAsync(Messages(70));

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 20));

        // Assert
        (await harness.GetMessagesAsync()).ShouldAllBe(m => m.ProcessedOnUtc != null);
        await harness.Publisher.Received(70).Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ShouldStopStartingBatches_AfterEightyPercentOfTheInterval()
    {
        // Arrange — every message takes a second; the interval is 30 s, so batches start for 24 s.
        var clock = new FakeTimeProvider();
        await using var harness = await OutboxJobTestHarness.CreateAsync(clock);
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                return Task.CompletedTask;
            });
        await harness.SeedAsync(Messages(70));

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 20, intervalInSeconds: 30));

        // Assert — the first batch ended at 20 s (within 24 s: another one starts); the second at 40 s (stop).
        var rows = await harness.GetMessagesAsync();
        rows.Count(m => m.ProcessedOnUtc != null).ShouldBe(40);
        rows.Count(m => m.ProcessedOnUtc == null).ShouldBe(30);
    }

    [Fact]
    public async Task Execute_ShouldReadAQuietOutboxOnce()
    {
        // Arrange
        await using var harness = await OutboxJobTestHarness.CreateAsync(new FakeTimeProvider());

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 20));

        // Assert
        harness.Queries.Selects.ShouldBe(1);
    }

    [Fact]
    public async Task Execute_ShouldNotTryAFailedMessageAgain_InTheSameRun()
    {
        // Arrange — the first of 25 messages always fails; batches of 20 keep coming back full.
        await using var harness = await OutboxJobTestHarness.CreateAsync(new FakeTimeProvider());
        var messages = Messages(25);
        var poison = messages[0];
        harness.Publisher
            .Publish(Arg.Is<IDomainEvent>(e => ((TestDomainEvent)e).Message == "poison"), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("always fails")));
        poison.Content = _serializer.Serialize(new TestDomainEvent(Guid.NewGuid(), "poison"));
        await harness.SeedAsync(messages);

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 20, maxRetryCount: 3));

        // Assert — the rest drained; the failed one was tried once, and waits for the next run.
        var rows = await harness.GetMessagesAsync();
        rows.Count(m => m.ProcessedOnUtc != null).ShouldBe(24);
        rows.Single(m => m.Id == poison.Id).RetryCount.ShouldBe(1);
    }

    private static OutboxMessage[] Messages(int count)
    {
        var start = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        return [.. Enumerable
            .Range(0, count)
            .Select(index =>
            {
                var message = CreateMessage(new TestDomainEvent(Guid.NewGuid(), $"message {index}"));
                message.OccurredOnUtc = start.AddSeconds(index);
                return message;
            })];
    }

    private static OutboxMessage CreateMessage(IDomainEvent domainEvent)
        => new()
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = domainEvent.GetType().FullName!,
            Content = _serializer.Serialize(domainEvent),
        };
}
