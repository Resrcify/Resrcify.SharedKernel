using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Messaging.Extensions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class OutboxDispatchTests
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private ServiceProvider _services = default!;

    public OutboxDispatchTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var collection = new ServiceCollection();

        collection.AddSingleton<HandlerInvocationTracker>();

        collection.AddMediator(cfg =>
            cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly()));

        collection.AddDbContext<TestDbContext>(options =>
            options
                .UseNpgsql(_pg.Container.GetConnectionString())
                .AddInterceptors(new InsertOutboxMessagesInterceptor()));

        _services = collection.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task ProcessOutboxMessagesJob_DispatchesDomainEvent_ToRegisteredHandler()
    {
        // Arrange — write a domain event through the interceptor.
        var aggregateId = Guid.NewGuid();
        await using (var seedScope = _services.CreateAsyncScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<TestDbContext>();
            var aggregate = new TestAggregate(aggregateId, "before");
            ctx.Aggregates.Add(aggregate);
            await ctx.SaveChangesAsync();

            ctx.ChangeTracker.Clear();
            var loaded = await ctx.Aggregates.SingleAsync(x => x.Id == aggregateId);
            loaded.ChangeName("after");
            await ctx.SaveChangesAsync(); // interceptor writes outbox row
        }

        // Act — drive the outbox job exactly like Quartz would.
        await using (var jobScope = _services.CreateAsyncScope())
        {
            var ctx = jobScope.ServiceProvider.GetRequiredService<TestDbContext>();
            var publisher = jobScope.ServiceProvider.GetRequiredService<IPublisher>();

            var job = new ProcessOutboxMessagesJob<TestDbContext>(ctx, publisher);

            var jobContext = Substitute.For<IJobExecutionContext>();
            jobContext.MergedJobDataMap.Returns(new JobDataMap
            {
                { "EventsAssemblyFullName", typeof(TestNameChangedEvent).Assembly.FullName! },
                { "ProcessBatchSize", 10 },
            });

            await job.Execute(jobContext);
        }

        // Assert — handler fired with the concrete event type, outbox row marked.
        var tracker = _services.GetRequiredService<HandlerInvocationTracker>();
        tracker.Received.Count.ShouldBe(1);
        tracker.Received.Single().AggregateId.ShouldBe(aggregateId);
        tracker.Received.Single().NewName.ShouldBe("after");

        await using var verifyScope = _services.CreateAsyncScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<TestDbContext>();
        var outboxRows = await verifyCtx.OutboxMessages.ToListAsync();
        outboxRows.Count.ShouldBe(1);
        outboxRows.Single().ProcessedOnUtc.ShouldNotBeNull();
    }
}
