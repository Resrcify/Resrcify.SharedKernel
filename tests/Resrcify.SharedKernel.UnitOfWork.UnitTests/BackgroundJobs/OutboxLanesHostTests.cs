using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Primitives;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxLanesHostTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(6, 160)]
    [InlineData(7, 300)]
    [InlineData(20, 300)]
    [InlineData(39, 300)]
    [InlineData(100, 300)]
    [InlineData(int.MaxValue, 300)]
    public void RetryDelay_ShouldDoubleFromFiveSecondsUpToFiveMinutes_WhenAMessageKeepsFailing(int failures, int seconds)
        => OutboxLanesHost<TestDbContext>.RetryDelay(failures).ShouldBe(TimeSpan.FromSeconds(seconds));

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 4, false)]
    [InlineData(1, 5, true)]
    [InlineData(2, 14, false)]
    [InlineData(2, 15, true)]
    public void DueForItsNextTry_ShouldWaitTheBackoffOfItsTriesSoFar_MeasuredFromWhenTheEventOccurred(
        int retryCount,
        int secondsSinceItOccurred,
        bool due)
    {
        // Kept in the database (RetryCount, OccurredOnUtc), so every instance running the lanes waits it out.
        var now = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            RetryCount = retryCount,
            OccurredOnUtc = now.AddSeconds(-secondsSinceItOccurred),
        };

        OutboxLanesHost<TestDbContext>.DueForItsNextTry(now, maxRetryCount: 3).Compile()(message).ShouldBe(due);
    }

    /// <summary>The clock never moves in these tests: a lane that waited for its poll would never get there.</summary>
    [Fact]
    public async Task ALane_ShouldPollAtOnce_WhenTheOutboxIsWoken()
    {
        await using var lanes = await LaneHost.StartAsync(maxConcurrency: 8);
        await lanes.SeedAsync(1);

        await lanes.Services.GetRequiredService<OutboxWakeUp<TestDbContext>>().WakeAsync();

        await lanes.WaitForPublishedAsync(1);
    }

    [Fact]
    public async Task ALane_ShouldWaitForItsPoll_WhenNothingWakesIt()
    {
        await using var lanes = await LaneHost.StartAsync(maxConcurrency: 8);
        await lanes.SeedAsync(1);

        await Task.Delay(TimeSpan.FromMilliseconds(300));

        lanes.Published.ShouldBe(0);
    }

    [Fact]
    public async Task ALane_ShouldStartItsNextMessage_AsSoonAsASlotFrees()
    {
        // Arrange — one slot, three messages, all due before the lane starts.
        await using var lanes = await LaneHost.StartAsync(maxConcurrency: 1, seed: 3);

        // Assert — each finished message frees the slot for the next, without a poll.
        await lanes.WaitForPublishedAsync(3);
    }

    /// <summary>A lane host on in-memory SQLite, polling every hour on a clock that doesn't move.</summary>
    private sealed class LaneHost : IAsyncDisposable
    {
        private static readonly SystemTextJsonOutboxSerializer Serializer = new();

        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private int _published;

        private LaneHost(SqliteConnection connection, ServiceProvider provider, IPublisher publisher)
        {
            _connection = connection;
            _provider = provider;
            publisher
                .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    Interlocked.Increment(ref _published);
                    return Task.CompletedTask;
                });
        }

        public IServiceProvider Services => _provider;

        /// <summary>The lanes' hosted service (the container owns and disposes it).</summary>
        private OutboxLanesHost<TestDbContext> Lanes
            => _provider.GetServices<IHostedService>().OfType<OutboxLanesHost<TestDbContext>>().Single();

        public int Published => Volatile.Read(ref _published);

        public static async Task<LaneHost> StartAsync(int maxConcurrency, int seed = 0)
        {
            // A named in-memory database: each context opens its own connection (the lanes and the test run at once),
            // and this one keeps the database alive.
            var connectionString = $"Data Source=lanes-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            var publisher = Substitute.For<IPublisher>();
            var queries = new QueryCounter();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider());
            services.AddScoped(_ => new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(queries)
                .Options));
            services.AddScoped<IUnitOfWork, UnitOfWork<TestDbContext>>();
            services.AddSingleton(publisher);
            services.AddSingleton<IOutboxSerializer>(Serializer);
            services.AddSingleton<IOutboxLaneEvent>(new OutboxLaneEvent("slow", typeof(TestDomainEvent)));
            services.AddOutboxLanes<TestDbContext>(lanes =>
            {
                lanes.MaxConcurrency = maxConcurrency;
                lanes.PollInterval = TimeSpan.FromHours(1);
            });
            var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();

            var host = new LaneHost(connection, provider, publisher);
            await host.SeedAsync(seed);
            var readsBefore = queries.ReadsDone;
            await host.Lanes.StartAsync(CancellationToken.None);
            // Its first poll, right at start, finds what was seeded so far.
            await WaitUntilAsync(() => queries.ReadsDone > readsBefore);
            return host;
        }

        public async Task SeedAsync(int count)
        {
            await using var scope = _provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            for (var index = 0; index < count; index++)
            {
                var domainEvent = new TestDomainEvent(Guid.NewGuid(), $"lane {index}");
                context.OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = typeof(TestDomainEvent).FullName!,
                    Content = Serializer.Serialize(domainEvent),
                    OccurredOnUtc = new DateTime(2026, 10, 6, 12, 0, index, DateTimeKind.Utc),
                });
            }
            await context.SaveChangesAsync();
        }

        public async Task WaitForPublishedAsync(int count)
        {
            await WaitUntilAsync(() => Published >= count);
            Published.ShouldBe(count);
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = TimeProvider.System.GetUtcNow().AddSeconds(10);
            while (!condition() && TimeProvider.System.GetUtcNow() < deadline)
                await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        public async ValueTask DisposeAsync()
        {
            await Lanes.StopAsync(CancellationToken.None);
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
