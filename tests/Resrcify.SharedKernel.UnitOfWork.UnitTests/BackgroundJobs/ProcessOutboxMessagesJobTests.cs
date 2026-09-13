using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
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

    private static OutboxMessage CreateMessage(IDomainEvent domainEvent)
        => new()
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = domainEvent.GetType().FullName!,
            Content = _serializer.Serialize(domainEvent),
        };
}
