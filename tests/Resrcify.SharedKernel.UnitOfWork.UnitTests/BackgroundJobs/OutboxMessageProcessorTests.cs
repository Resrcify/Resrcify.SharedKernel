using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

/// <summary>
/// Recording a failed try when another instance may be trying the same message: the try is counted in the database,
/// not from this try's snapshot of the row.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxMessageProcessorTests
{
    private static readonly SystemTextJsonOutboxSerializer Serializer = new();

    [Fact]
    public async Task ProcessAsync_ShouldCountTheFailedTryInTheDatabase_WhenAnotherInstanceCountedOneSinceTheSnapshot()
    {
        await using var harness = await FailingHarnessAsync();
        var row = Message(retryCount: 1);   // another instance's failure is already counted
        await harness.SeedAsync(row);

        var outcome = await ProcessAsync(harness, Snapshot(row, retryCount: 0), maxRetryCount: 5);

        outcome.ShouldBe(OutboxProcessOutcome.Failed);
        var stored = (await harness.GetMessagesAsync()).Single();
        stored.RetryCount.ShouldBe(2);
        stored.ProcessedOnUtc.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessAsync_ShouldNotMarkAMessageGivenUp_WhenAnotherInstancePublishedItMeanwhile()
    {
        await using var harness = await FailingHarnessAsync();
        var publishedAt = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        var row = Message(retryCount: 2);
        row.ProcessedOnUtc = publishedAt;   // processed by another instance after this one read it
        await harness.SeedAsync(row);

        await ProcessAsync(harness, Snapshot(row, retryCount: 2), maxRetryCount: 3);

        var stored = (await harness.GetMessagesAsync()).Single();
        stored.ProcessedOnUtc.ShouldBe(publishedAt);
        stored.RetryCount.ShouldBe(2);
        stored.Error.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessAsync_ShouldGiveUp_WhenTheCountInTheDatabaseReachesTheLimit()
    {
        await using var harness = await FailingHarnessAsync();
        var row = Message(retryCount: 2);
        await harness.SeedAsync(row);

        await ProcessAsync(harness, Snapshot(row, retryCount: 1), maxRetryCount: 3);

        var stored = (await harness.GetMessagesAsync()).Single();
        stored.RetryCount.ShouldBe(3);
        stored.ProcessedOnUtc.ShouldBe(OutboxMessage.GivenUpProcessedOnUtc);
        stored.Error.ShouldNotBeNull().ShouldStartWith("Gave up at ");
    }

    private static Task<OutboxProcessOutcome> ProcessAsync(
        OutboxJobTestHarness harness,
        OutboxMessageToProcess snapshot,
        int maxRetryCount)
        => OutboxMessageProcessor<TestDbContext>.ProcessAsync(
            harness.Services.GetRequiredService<IServiceScopeFactory>(),
            snapshot,
            claim: null,
            maxRetryCount,
            CancellationToken.None);

    private static async Task<OutboxJobTestHarness> FailingHarnessAsync()
    {
        var harness = await OutboxJobTestHarness.CreateAsync();
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("the handler failed")));
        return harness;
    }

    private static OutboxMessage Message(int retryCount)
    {
        var domainEvent = new TestDomainEvent(Guid.NewGuid(), "contended");
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = new DateTime(2026, 10, 7, 11, 0, 0, DateTimeKind.Utc),
            Type = domainEvent.GetType().FullName!,
            Content = Serializer.Serialize(domainEvent),
            RetryCount = retryCount,
        };
    }

    private static OutboxMessageToProcess Snapshot(OutboxMessage row, int retryCount)
        => new(row.Id, row.Type, row.Content, retryCount, row.OccurredOnUtc);
}
