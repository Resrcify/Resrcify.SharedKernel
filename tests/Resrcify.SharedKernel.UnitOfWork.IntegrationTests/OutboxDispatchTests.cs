using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quartz;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Messaging.Extensions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
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
    private static readonly SystemTextJsonOutboxSerializer _serializer = new();

    private readonly PostgresFixture _pg;
    private ServiceProvider _services = default!;

    public OutboxDispatchTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var collection = new ServiceCollection();

        collection.AddSingleton<HandlerInvocationTracker>();

        collection.AddMediator(cfg =>
            cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly()));

        // Each test runs against its own fresh database in the shared container,
        // so tests are fully isolated without truncating or sharing state.
        // Resolved once here — the AddDbContext options callback runs per scope.
        var connectionString = _pg.CreateIsolatedConnectionString();
        collection.AddDbContext<TestDbContext>(options =>
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(new InsertOutboxMessagesInterceptor(_serializer)));

        // Read side: unit of work, serializer for the job, and the job itself.
        collection.AddOutboxProcessing<TestDbContext>(_serializer);

        _services = collection.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await ctx.Database.EnsureDeletedAsync();
        }

        await _services.DisposeAsync();
    }

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
        await RunOutboxJobAsync();

        // Assert — handler fired with the concrete event type, outbox row marked.
        var tracker = _services.GetRequiredService<HandlerInvocationTracker>();
        tracker.Received.Count.ShouldBe(1);
        tracker.Received.Single().AggregateId.ShouldBe(aggregateId);
        tracker.Received.Single().NewName.ShouldBe("after");

        var outboxRows = await GetOutboxMessagesAsync();
        outboxRows.Count.ShouldBe(1);
        outboxRows.Single().ProcessedOnUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task ProcessOutboxMessagesJob_CommitsHandlerWork_AndMarkProcessed_Atomically()
    {
        // Arrange — an event whose handler persists an aggregate and succeeds.
        var aggregateId = Guid.NewGuid();
        var messageId = await SeedOutboxMessageAsync(
            new TestSideEffectEvent(Guid.NewGuid(), aggregateId, ShouldThrow: false));

        // Act
        await RunOutboxJobAsync();

        // Assert — the handler's write and the "mark processed" both committed.
        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        (await ctx.Aggregates.AnyAsync(a => a.Id == aggregateId)).ShouldBeTrue();

        var message = await ctx.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Id == messageId);
        message.ProcessedOnUtc.ShouldNotBeNull();
        message.Error.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessOutboxMessagesJob_RollsBackHandlerWork_WhenHandlerThrows()
    {
        // Arrange — an event whose handler persists an aggregate, then throws.
        var aggregateId = Guid.NewGuid();
        var messageId = await SeedOutboxMessageAsync(
            new TestSideEffectEvent(Guid.NewGuid(), aggregateId, ShouldThrow: true));

        // Act
        await RunOutboxJobAsync();

        // Assert — the handler's write was rolled back together with the (skipped)
        // "mark processed": the aggregate is absent and the message still pending.
        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        (await ctx.Aggregates.AnyAsync(a => a.Id == aggregateId)).ShouldBeFalse();

        var message = await ctx.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Id == messageId);
        message.ProcessedOnUtc.ShouldBeNull();
        message.RetryCount.ShouldBe(1);
        message.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task ProcessOutboxMessagesJob_IsolatesMessages_OneFailureLeavesOthersCommitted()
    {
        // Arrange — one message succeeds, one fails, in the same batch.
        var committedAggregateId = Guid.NewGuid();
        var rolledBackAggregateId = Guid.NewGuid();
        var committedMessageId = await SeedOutboxMessageAsync(
            new TestSideEffectEvent(Guid.NewGuid(), committedAggregateId, ShouldThrow: false));
        var rolledBackMessageId = await SeedOutboxMessageAsync(
            new TestSideEffectEvent(Guid.NewGuid(), rolledBackAggregateId, ShouldThrow: true));

        // Act
        await RunOutboxJobAsync();

        // Assert — each message committed or rolled back independently.
        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        (await ctx.Aggregates.AnyAsync(a => a.Id == committedAggregateId)).ShouldBeTrue();
        (await ctx.Aggregates.AnyAsync(a => a.Id == rolledBackAggregateId)).ShouldBeFalse();

        var committed = await ctx.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Id == committedMessageId);
        var rolledBack = await ctx.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Id == rolledBackMessageId);
        committed.ProcessedOnUtc.ShouldNotBeNull();
        rolledBack.ProcessedOnUtc.ShouldBeNull();
        rolledBack.RetryCount.ShouldBe(1);
    }

    [Fact]
    public async Task OutboxMessageConfiguration_CreatesCoveringPartialIndex()
    {
        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var indexDef = await ctx.Database
            .SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes "
                + "WHERE schemaname = {0} AND indexname = {1}",
                TestDbContext.OutboxSchema,
                "IX_OutboxMessages_Unprocessed")
            .SingleOrDefaultAsync();

        indexDef.ShouldNotBeNull();
        indexDef.ShouldContain("ProcessedOnUtc");
        indexDef.ShouldContain("OccurredOnUtc");
        indexDef.ShouldContain("INCLUDE", Case.Insensitive);
        indexDef.ShouldContain("WHERE", Case.Insensitive);
    }

    [Fact]
    public async Task OutboxMessageConfiguration_PlacesTableInConfiguredSchema()
    {
        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var schema = await ctx.Database
            .SqlQueryRaw<string>(
                "SELECT table_schema AS \"Value\" FROM information_schema.tables "
                + "WHERE table_name = {0}",
                "OutboxMessages")
            .SingleOrDefaultAsync();

        schema.ShouldBe(TestDbContext.OutboxSchema);
    }

    // Drives the job the way Quartz would: it resolves a fresh scope per message.
    private async Task RunOutboxJobAsync(int maxRetryCount = 3)
    {
        var job = new ProcessOutboxMessagesJob<TestDbContext>(
            _services.GetRequiredService<IServiceScopeFactory>());

        var jobContext = Substitute.For<IJobExecutionContext>();
        jobContext.MergedJobDataMap.Returns(new JobDataMap
        {
            { "ProcessBatchSize", 10 },
            { "ProcessMaxRetryCount", maxRetryCount },
        });

        await job.Execute(jobContext);
    }

    private async Task<Guid> SeedOutboxMessageAsync(IDomainEvent domainEvent)
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = domainEvent.GetType().FullName!,
            Content = _serializer.Serialize(domainEvent),
        };

        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        ctx.OutboxMessages.Add(message);
        await ctx.SaveChangesAsync();
        return message.Id;
    }

    private async Task<System.Collections.Generic.List<OutboxMessage>> GetOutboxMessagesAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        return await ctx.OutboxMessages.AsNoTracking().ToListAsync();
    }
}
