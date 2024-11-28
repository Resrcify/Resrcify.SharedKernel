using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
using MediatR;
using NSubstitute;
using FluentAssertions;
using Xunit;
using System.Linq;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using System.Text.Json;
using Resrcify.SharedKernel.UnitOfWork.Converters;
using System.Reflection;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

public class ProcessOutboxMessagesJobTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new DomainEventConverter() }
    };

    [Fact]
    public async Task Execute_ShouldProcessOutboxMessagesAndPublishDomainEvents()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase")
            .Options;

        using var dbContext = new TestDbContext(options);
        var publisherMock = Substitute.For<IPublisher>();
        var jobContextMock = Substitute.For<IJobExecutionContext>();

        var dataMap = new JobDataMap
        {
            { "EventsAssemblyFullName", Assembly.GetExecutingAssembly().FullName! },
            { "ProcessBatchSize", 2 }
        };
        jobContextMock.MergedJobDataMap.Returns(dataMap);

        var job = new ProcessOutboxMessagesJob<TestDbContext>(dbContext, publisherMock);

        var outboxMessages = Enumerable
            .Repeat(new TestDomainEvent(Guid.NewGuid(), "Test message"), 2)
            .Select(domainEvent => new OutboxMessage
            {
                Id = Guid.NewGuid(),
                OccurredOnUtc = DateTime.UtcNow,
                Type = domainEvent.GetType().FullName!,
                Content = JsonSerializer.Serialize(domainEvent, _jsonOptions)
            })
            .ToList();

        await dbContext.OutboxMessages.AddRangeAsync(outboxMessages);
        await dbContext.SaveChangesAsync();

        // Act
        await job.Execute(jobContextMock);

        // Assert
        await publisherMock.Received(outboxMessages.Count).Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
        await dbContext.SaveChangesAsync();
        outboxMessages.ForEach(m => m.ProcessedOnUtc.Should().NotBeNull());
    }
}
