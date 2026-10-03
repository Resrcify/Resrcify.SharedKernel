using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Quartz;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Abstractions.Messaging;
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

        // Assert — the message is attempted exactly maxRetryCount times, then skipped.
        await harness.Publisher
            .Received(maxRetryCount)
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());

        var row = (await harness.GetMessagesAsync()).Single();
        row.RetryCount.ShouldBe(maxRetryCount);
        row.ProcessedOnUtc.ShouldBeNull();
        row.Error.ShouldNotBeNull();
    }

    /// <summary>
    /// Quartz 4 removed the typed JobDataMap getters, so the job now reads the stored object itself. These
    /// pin how it treats each shape a batch size can arrive in: honoured when it is a usable number however
    /// it was stored, and the default of 20 otherwise - never an exception that fails the job.
    /// </summary>
    [Theory]
    [MemberData(nameof(BatchSizeShapes))]
    public async Task Execute_ShouldHonourBatchSize_HoweverItWasStored(object? storedBatchSize, int expectedProcessed)
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

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(data));

        // Assert
        (await harness.GetMessagesAsync())
            .Count(m => m.ProcessedOnUtc != null)
            .ShouldBe(expectedProcessed);
    }

    public static TheoryData<object?, int> BatchSizeShapes => new()
    {
        { 2, 2 },               // stored as an int, as the setup does
        { 2L, 2 },              // a long, as a persistent store may hand it back
        { "2", 2 },             // a string, as configuration supplies it
        { "not-a-number", 5 },  // unusable: falls back to the default batch of 20
        { long.MaxValue, 5 },   // out of int range: falls back rather than overflowing
        { null, 5 },            // absent: the default batch of 20
    };

    private static OutboxMessage CreateMessage(IDomainEvent domainEvent)
        => new()
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = domainEvent.GetType().FullName!,
            Content = _serializer.Serialize(domainEvent),
        };
}
