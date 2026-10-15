using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task AFailedMessage_ShouldWaitTheLanesConfiguredRetryDelay()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        await using var lanes = await LaneHost.StartAsync(
            maxConcurrency: 8,
            seed: 1,
            time: time,
            publishFails: true,
            configure: options => options.RetryDelay = TimeSpan.FromSeconds(30));

        await lanes.WaitForTriesAsync(1);

        (await lanes.NextAttemptAsync()).ShouldBe(time.GetUtcNow().UtcDateTime.AddSeconds(30));
    }

    [Fact]
    public async Task ALane_ShouldLogEachFailedTry_AndTheGiveUpAtError()
    {
        // No wait between tries: the freed slot takes the message again at once, until it gives up.
        using var logs = new RecordingLoggerProvider();
        await using var lanes = await LaneHost.StartAsync(
            maxConcurrency: 8,
            seed: 1,
            publishFails: true,
            configure: options =>
            {
                options.MaxRetryCount = 2;
                options.RetryDelay = TimeSpan.Zero;
            },
            logs: logs);

        await lanes.WaitForTriesAsync(2);
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(10);
        while (logs.Outbox().Count < 2 && TimeProvider.System.GetUtcNow() < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(20));

        var logged = logs.Outbox();
        logged.Count.ShouldBe(2);
        logged[0].Level.ShouldBe(LogLevel.Error);
        logged[0].Message.ShouldContain("(TestDomainEvent, TestDbContext) failed try 1 of 2");
        logged[0].Exception.ShouldBeOfType<InvalidOperationException>();
        logged[1].Level.ShouldBe(LogLevel.Error);
        logged[1].Message.ShouldContain("gave up after try 2 (non-transient failures get 2 tries)");
        logged[1].Message.ShouldEndWith("InvalidOperationException: The handler failed (the test's).");
    }

    [Fact]
    public void AddOutboxLanes_ShouldThrow_WhenTheLongestRetryDelayIsShorterThanTheFirst()
        => Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddOutboxLanes<TestDbContext>(options =>
            {
                options.RetryDelay = TimeSpan.FromMinutes(1);
                options.MaxRetryDelay = TimeSpan.FromSeconds(30);
            }))
            .ParamName.ShouldBe(nameof(OutboxLaneOptions.MaxRetryDelay));

    [Fact]
    public async Task ALane_ShouldTryATransientlyFailingMessageTwelveTimes_WithBackoff_ThenGiveUp()
    {
        // A downstream outage: the default limits (3, and 12 for a transient failure) and backoff.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        await using var lanes = await LaneHost.StartAsync(
            maxConcurrency: 8,
            seed: 1,
            time: time,
            failWith: _ => new HttpRequestException("the downstream service is down", null, HttpStatusCode.ServiceUnavailable));

        var waits = new List<int>();
        for (var tries = 1; tries < 12; tries++)
        {
            await lanes.WaitForTriesAsync(tries);
            var wait = (await lanes.NextAttemptAsync()).ShouldNotBeNull() - time.GetUtcNow().UtcDateTime;
            waits.Add((int)wait.TotalSeconds);
            time.Advance(wait);
            await lanes.Services.GetRequiredService<OutboxWakeUp<TestDbContext>>().WakeAsync();
        }
        await lanes.WaitForTriesAsync(12);
        await lanes.WaitForGivenUpAsync();
        time.Advance(TimeSpan.FromHours(1));
        await lanes.Services.GetRequiredService<OutboxWakeUp<TestDbContext>>().WakeAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        waits.ShouldBe([5, 10, 20, 40, 80, 160, 300, 300, 300, 300, 300]);
        lanes.Tries.ShouldBe(12);
        (await lanes.RetryCountAsync()).ShouldBe(12);
    }

    [Fact]
    public async Task ALane_ShouldGiveUpANonTransientlyFailingMessage_AfterThreeTries()
    {
        await using var lanes = await LaneHost.StartAsync(
            maxConcurrency: 8,
            seed: 1,
            publishFails: true,
            configure: options => options.RetryDelay = TimeSpan.Zero);

        await lanes.WaitForTriesAsync(3);
        await lanes.WaitForGivenUpAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        lanes.Tries.ShouldBe(3);
    }

    [Fact]
    public async Task ALane_ShouldGiveUpAtOnce_WhenANonTransientFailureFollowsTransientRetriesPastTheLimit()
    {
        // Five transient failures (past the 3 tries a non-transient one gets), then the handler's bug: no more tries.
        await using var lanes = await LaneHost.StartAsync(
            maxConcurrency: 8,
            seed: 1,
            configure: options => options.RetryDelay = TimeSpan.Zero,
            failWith: attempt => attempt <= 5
                ? new TimeoutException("the downstream service didn't answer")
                : new InvalidOperationException("The handler failed (the test's)."));

        await lanes.WaitForTriesAsync(6);
        await lanes.WaitForGivenUpAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        lanes.Tries.ShouldBe(6);
    }

    [Fact]
    public async Task ALane_ShouldReadAMessageRetryingTransientlyPastMaxRetryCount()
    {
        await using var lanes = await LaneHost.StartAsync(maxConcurrency: 8);
        await lanes.SeedAsync(1, retryCount: 7);

        await lanes.Services.GetRequiredService<OutboxWakeUp<TestDbContext>>().WakeAsync();

        await lanes.WaitForPublishedAsync(1);
    }

    [Theory]
    [InlineData(5, 4, nameof(OutboxLaneOptions.MaxTransientRetryCount))]
    [InlineData(0, 12, nameof(OutboxLaneOptions.MaxRetryCount))]
    public void AddOutboxLanes_ShouldThrow_WhenTheRetryLimitsAreOutOfOrder(
        int maxRetryCount,
        int maxTransientRetryCount,
        string parameter)
        => Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddOutboxLanes<TestDbContext>(options =>
            {
                options.MaxRetryCount = maxRetryCount;
                options.MaxTransientRetryCount = maxTransientRetryCount;
            }))
            .ParamName.ShouldBe(parameter);

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
        private int _tries;

        private LaneHost(
            SqliteConnection connection,
            ServiceProvider provider,
            IPublisher publisher,
            bool publishFails,
            Func<int, Exception>? failWith)
        {
            _connection = connection;
            _provider = provider;
            publisher
                .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    var attempt = Interlocked.Increment(ref _tries);
                    if (failWith is not null)
                        throw failWith(attempt);
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

        /// <summary>How often a handler was called, failing or not.</summary>
        public int Tries => Volatile.Read(ref _tries);

        /// <param name="connectionString">The database, shared by hosts standing for instances of one service.</param>
        /// <param name="failWith">The exception each try (numbered from 1) fails with; none fails when not given.</param>
        public static async Task<LaneHost> StartAsync(
            int maxConcurrency,
            int seed = 0,
            string? connectionString = null,
            FakeTimeProvider? time = null,
            bool publishFails = false,
            Action<OutboxLaneOptions>? configure = null,
            RecordingLoggerProvider? logs = null,
            Func<int, Exception>? failWith = null)
        {
            // A named in-memory database: each context opens its own connection (the lanes and the test run at once),
            // and this one keeps the database alive.
            connectionString ??= $"Data Source=lanes-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            var publisher = Substitute.For<IPublisher>();
            var queries = new QueryCounter();
            var services = new ServiceCollection();
            services.AddLogging(logging =>
            {
                if (logs is not null)
                    logging.AddProvider(logs);
            });
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
                configure?.Invoke(lanes);
            });
            var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();

            var host = new LaneHost(connection, provider, publisher, publishFails, failWith);
            await host.SeedAsync(seed);
            var readsBefore = queries.ReadsDone;
            await host.Lanes.StartAsync(CancellationToken.None);
            // Its first poll, right at start, finds what was seeded so far.
            await WaitUntilAsync(() => queries.ReadsDone > readsBefore);
            return host;
        }

        public async Task SeedAsync(int count, int retryCount = 0)
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
                    RetryCount = retryCount,
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

        /// <summary>Waits until the (only) message is marked given up.</summary>
        public async Task WaitForGivenUpAsync()
        {
            var deadline = TimeProvider.System.GetUtcNow().AddSeconds(10);
            while ((await ReadMessageAsync()).ProcessedOnUtc is null && TimeProvider.System.GetUtcNow() < deadline)
                await Task.Delay(TimeSpan.FromMilliseconds(20));
            (await ReadMessageAsync()).ProcessedOnUtc.ShouldBe(OutboxMessage.GivenUpProcessedOnUtc);
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
