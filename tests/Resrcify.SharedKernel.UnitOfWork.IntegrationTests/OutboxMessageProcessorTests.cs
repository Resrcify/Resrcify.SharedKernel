using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Quartz;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests;

/// <summary>How the outbox processes a message whose handler times out, or runs a transactional command.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class OutboxMessageProcessorTests(PostgresFixture pg)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly SystemTextJsonOutboxSerializer Serializer = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
    private ServiceProvider _services = default!;

    public async Task InitializeAsync()
    {
        var connectionString = pg.CreateIsolatedConnectionString();
        var services = new ServiceCollection();
        services.AddSingleton<HandlerInvocationTracker>();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddMediator(cfg => cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly()));
        services.AddDbContext<TestDbContext>(options => options
            .UseNpgsql(connectionString)
            .AddInterceptors(new InsertOutboxMessagesInterceptor(Serializer)));
        services.AddOutboxProcessing<TestDbContext>(Serializer);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureDeletedAsync();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task Process_ShouldRecordAFailureAndGoOn_WhenAHandlerTimesOut()
    {
        var timedOut = await SeedAsync(new TestTimeoutEvent(Guid.NewGuid()));
        await Task.Delay(10);   // processed oldest first: the timeout comes first in the batch
        var next = await SeedAsync(new TestSideEffectEvent(Guid.NewGuid(), Guid.NewGuid(), ShouldThrow: false));

        await RunJobAsync();

        var failed = await FindAsync(timedOut);
        failed.ProcessedOnUtc.ShouldBeNull();
        failed.RetryCount.ShouldBe(1);
        failed.Error!.ShouldContain("timed out");
        (await FindAsync(next)).ProcessedOnUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Process_ShouldCommitATransactionalCommand_WhenAHandlerRunsOneInsideTheOutboxTransaction()
    {
        var aggregateId = Guid.NewGuid();
        var message = await SeedAsync(new TestTransactionalCommandEvent(Guid.NewGuid(), aggregateId, CommandFails: false));

        await RunJobAsync();

        (await FindAsync(message)).ProcessedOnUtc.ShouldNotBeNull();
        (await AggregateNamesAsync()).ShouldBe(["before-command", "written-by-command"], ignoreOrder: true);
    }

    [Fact]
    public async Task Process_ShouldUndoOnlyTheCommandsWork_WhenTheCommandFails()
    {
        var message = await SeedAsync(new TestTransactionalCommandEvent(Guid.NewGuid(), Guid.NewGuid(), CommandFails: true));

        await RunJobAsync();

        (await FindAsync(message)).ProcessedOnUtc.ShouldNotBeNull();
        (await AggregateNamesAsync()).ShouldBe(["before-command"]);
    }

    [Fact]
    public async Task Process_ShouldStampProcessedOnFromTheClock_WhenTheMessageIsPublished()
    {
        var message = await SeedAsync(new TestSideEffectEvent(Guid.NewGuid(), Guid.NewGuid(), ShouldThrow: false));
        _clock.Advance(TimeSpan.FromMinutes(3));

        await RunJobAsync();

        (await FindAsync(message)).ProcessedOnUtc.ShouldBe(_clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Process_ShouldCountEachOutcome_WhenMessagesSucceedOrFail()
    {
        using var handled = new HandledOutcomes();
        await SeedAsync(new TestTimeoutEvent(Guid.NewGuid()));
        await SeedAsync(new TestTransactionalCommandEvent(Guid.NewGuid(), Guid.NewGuid(), CommandFails: false));

        await RunJobAsync();

        handled.Count("TestTimeoutEvent", "retrying").ShouldBe(1);
        handled.Count("TestTransactionalCommandEvent", "processed").ShouldBe(1);
    }

    [Fact]
    public async Task Process_ShouldTakeTheMessageOutOfThePolledRows_WhenItsLastTryFails()
    {
        var message = await SeedAsync(new TestSideEffectEvent(Guid.NewGuid(), Guid.NewGuid(), ShouldThrow: true));
        for (var attempt = 0; attempt < 3; attempt++)
            await RunJobAsync();

        // A limit it is under: the poll would try it again if it still read it.
        await RunJobAsync(maxRetryCount: 10);

        var givenUp = await FindAsync(message);
        givenUp.ProcessedOnUtc.ShouldBe(OutboxMessage.GivenUpProcessedOnUtc);
        givenUp.RetryCount.ShouldBe(3);
        givenUp.Error!.ShouldStartWith("Gave up at 2026-10-05T12:00:00.0000000Z after 3 tries.");
        (await UnprocessedCountAsync()).ShouldBe(0);   // the rows the poll's partial index holds
    }

    /// <summary>The <c>outbox.messages.handled</c> measurements, by event and outcome.</summary>
    private sealed class HandledOutcomes : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _counts = new(StringComparer.Ordinal);

        public HandledOutcomes()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == OutboxDiagnostics.MeterName && instrument.Name == "outbox.messages.handled")
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                string? eventName = null, outcome = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "event") eventName = tag.Value as string;
                    if (tag.Key == "outcome") outcome = tag.Value as string;
                }
                _counts.AddOrUpdate($"{eventName}/{outcome}", value, (_, total) => total + value);
            });
            _listener.Start();
        }

        public long Count(string eventName, string outcome)
            => _counts.TryGetValue($"{eventName}/{outcome}", out var count) ? count : 0;

        public void Dispose()
            => _listener.Dispose();
    }

    private async Task RunJobAsync(int maxRetryCount = 3)
    {
        var job = new ProcessOutboxMessagesJob<TestDbContext>(_services.GetRequiredService<IServiceScopeFactory>());
        var context = Substitute.For<IJobExecutionContext>();
        context.MergedJobDataMap.Returns(new JobDataMap { { "ProcessBatchSize", 10 }, { "ProcessMaxRetryCount", maxRetryCount } });
        await job.Execute(context);
    }

    private async Task<int> UnprocessedCountAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().OutboxMessages.CountAsync(m => m.ProcessedOnUtc == null);
    }

    private async Task<Guid> SeedAsync(IDomainEvent domainEvent)
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = domainEvent.GetType().FullName!,
            Content = Serializer.Serialize(domainEvent),
        };
        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        ctx.OutboxMessages.Add(message);
        await ctx.SaveChangesAsync();
        return message.Id;
    }

    private async Task<OutboxMessage> FindAsync(Guid id)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private async Task<string[]> AggregateNamesAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Aggregates.AsNoTracking().Select(a => a.Name).ToArrayAsync();
    }
}
