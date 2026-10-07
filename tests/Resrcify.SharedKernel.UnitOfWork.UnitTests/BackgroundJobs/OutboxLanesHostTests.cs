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

    [Fact]
    public async Task AFailedMessage_ShouldWaitItsBackoff_OnEveryInstance_WhenItWaitedLongBeforeItsFirstTry()
    {
        // A day-old message in a backlog: the schedule used to count from when the event occurred, so its waits had all
        // passed, and a second instance (which hadn't failed it itself) tried it again at once.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        var database = $"Data Source=lanes-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var first = await LaneHost.StartAsync(maxConcurrency: 8, seed: 1, database, time, publishFails: true);
        await first.WaitForTriesAsync(1);
        (await first.NextAttemptAsync()).ShouldBe(time.GetUtcNow().UtcDateTime.AddSeconds(5));

        await using var second = await LaneHost.StartAsync(maxConcurrency: 8, seed: 0, database, time, publishFails: true);
        await second.Services.GetRequiredService<OutboxWakeUp<TestDbContext>>().WakeAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        (await second.RetryCountAsync()).ShouldBe(1);

        time.Advance(TimeSpan.FromSeconds(5));
        await second.Services.GetRequiredService<OutboxWakeUp<TestDbContext>>().WakeAsync();
        await second.WaitForTriesAsync(2);
        (await second.NextAttemptAsync()).ShouldBe(time.GetUtcNow().UtcDateTime.AddSeconds(10));
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

        private LaneHost(SqliteConnection connection, ServiceProvider provider, IPublisher publisher, bool publishFails)
        {
            _connection = connection;
            _provider = provider;
            publisher
                .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    if (publishFails)
                        throw new InvalidOperationException("The handler failed (the test's).");
                    Interlocked.Increment(ref _published);
                    return Task.CompletedTask;
                });
        }

        public IServiceProvider Services => _provider;

        /// <summary>The lanes' hosted service (the container owns and disposes it).</summary>
        private OutboxLanesHost<TestDbContext> Lanes
            => _provider.GetServices<IHostedService>().OfType<OutboxLanesHost<TestDbContext>>().Single();

        public int Published => Volatile.Read(ref _published);

        /// <param name="connectionString">The database, shared by hosts standing for instances of one service.</param>
        public static async Task<LaneHost> StartAsync(
            int maxConcurrency,
            int seed = 0,
            string? connectionString = null,
            FakeTimeProvider? time = null,
            bool publishFails = false)
        {
            // A named in-memory database: each context opens its own connection (the lanes and the test run at once),
            // and this one keeps the database alive.
            connectionString ??= $"Data Source=lanes-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            var publisher = Substitute.For<IPublisher>();
            var queries = new QueryCounter();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(time ?? new FakeTimeProvider());
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

            var host = new LaneHost(connection, provider, publisher, publishFails);
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

        /// <summary>Waits until the (only) message has failed <paramref name="count"/> tries.</summary>
        public async Task WaitForTriesAsync(int count)
        {
            var deadline = TimeProvider.System.GetUtcNow().AddSeconds(10);
            while (await RetryCountAsync() < count && TimeProvider.System.GetUtcNow() < deadline)
                await Task.Delay(TimeSpan.FromMilliseconds(20));
            (await RetryCountAsync()).ShouldBe(count);
        }

        public async Task<int> RetryCountAsync()
            => (await ReadMessageAsync()).RetryCount;

        public async Task<DateTime?> NextAttemptAsync()
            => (await ReadMessageAsync()).NextAttemptOnUtc;

        private async Task<OutboxMessage> ReadMessageAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TestDbContext>().OutboxMessages.AsNoTracking().SingleAsync();
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
