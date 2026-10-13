using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Outbox;

/// <summary>
/// The outbox wake-up end to end: a save's <c>NOTIFY</c>, the listener, and the outbox job or lane it wakes. The job and
/// the lanes poll every 10 minutes here, so an event handled within seconds was woken.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class PostgresOutboxListenerTests(
    PostgresFixture postgres,
    ITestOutputHelper output)
    : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan WellUnderThePoll = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ASavedEvent_ShouldBeProcessedWellBeforeTheNextPoll()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => db.WithOutbox().WithOutboxWakeUp());
        await host.StartAndListenAsync();

        var saved = TimeProvider.System.GetTimestamp();
        var shardId = await host.SaveShardAsync(shard => shard.Rename("woken"));
        var handled = await host.Tracker.HandledAsync(shardId).WaitAsync(WellUnderThePoll);

        var latency = TimeProvider.System.GetElapsedTime(saved, handled);
        output.WriteLine($"Saved to handled: {latency.TotalMilliseconds:F0} ms (the poll is every {TestHost.PollInterval}).");
        latency.ShouldBeLessThan(WellUnderThePoll);
    }

    /// <summary>The control: without the wake-up the same event waits for the poll, 10 minutes away.</summary>
    [Fact]
    public async Task WithoutTheWakeUp_ASavedEvent_ShouldWaitForTheNextPoll()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => db.WithOutbox());
        await host.Host.StartAsync();

        var shardId = await host.SaveShardAsync(shard => shard.Rename("not woken"));

        var handled = host.Tracker.HandledAsync(shardId);
        (await Task.WhenAny(handled, Task.Delay(TimeSpan.FromSeconds(3)))).ShouldNotBe(handled);
    }

    [Fact]
    public async Task ALaneEvent_ShouldBeProcessedWellBeforeTheLanesNextPoll()
    {
        await using var host = await TestHost.CreateAsync(
            postgres,
            db => db.WithOutbox().WithOutboxWakeUp(),
            services => services.AddSingleton<IOutboxLaneEvent>(new OutboxLaneEvent("ranks", typeof(RanksRequested))));
        await host.StartAndListenAsync();

        var saved = TimeProvider.System.GetTimestamp();
        var shardId = await host.SaveShardAsync(shard => shard.RequestRanks());
        var handled = await host.Tracker.HandledAsync(shardId).WaitAsync(WellUnderThePoll);

        TimeProvider.System.GetElapsedTime(saved, handled).ShouldBeLessThan(WellUnderThePoll);
    }

    [Fact]
    public async Task TheListener_ShouldReconnect_AfterLosingItsConnection()
    {
        await using var host = await TestHost.CreateAsync(
            postgres,
            db => db.WithOutbox().WithOutboxWakeUp(wakeUp => wakeUp.ReconnectDelay = TimeSpan.FromMilliseconds(100)));
        await host.StartAndListenAsync();

        await host.KillListenerAsync();
        await host.WaitForListenerAsync();
        var saved = TimeProvider.System.GetTimestamp();
        var shardId = await host.SaveShardAsync(shard => shard.Rename("after a reconnect"));
        var handled = await host.Tracker.HandledAsync(shardId).WaitAsync(WellUnderThePoll);

        TimeProvider.System.GetElapsedTime(saved, handled).ShouldBeLessThan(WellUnderThePoll);
    }

    [Fact]
    public async Task ASaveRolledBack_ShouldNotWakeTheOutbox_NorLeaveAMessage()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => db.WithOutbox().WithOutboxWakeUp());
        await host.StartAndListenAsync();
        // A listener of the test's own: it sees any NOTIFY the save sends, wherever it was sent.
        await using var observer = new NpgsqlConnection(host.ConnectionString);
        await observer.OpenAsync();
        await using (var listen = new NpgsqlCommand("LISTEN resrcify_outbox", observer))
            await listen.ExecuteNonQueryAsync();
        var shard = new Shard(Guid.NewGuid(), "rolled back");
        shard.Rename("never committed");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Shards.Add(shard);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
            await transaction.RollbackAsync();
        }

        (await observer.WaitAsync(1000)).ShouldBeFalse();   // no notification: it was part of what rolled back
        await using (var scope = host.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<TestDbContext>().OutboxMessages.CountAsync()).ShouldBe(0);
        var handled = host.Tracker.HandledAsync(shard.Id);
        (await Task.WhenAny(handled, Task.Delay(TimeSpan.FromSeconds(1)))).ShouldNotBe(handled);
    }

    [Fact]
    public async Task ABurstOfNotifications_ShouldWakeTheOutboxOnce()
    {
        using var wakeUps = new WakeUpLog();
        await using var host = await TestHost.CreateAsync(
            postgres,
            db => db.WithOutbox().WithOutboxWakeUp(),
            services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(wakeUps)));
        await host.StartAndListenAsync();
        await WaitUntilAsync(() => wakeUps.Count >= 1);   // the wake-up every connect gives
        var afterConnect = wakeUps.Count;

        await host.NotifyAsync(times: 100);
        // Until the burst has woken it, then until the wake-ups stop: a fixed wait failed on a loaded machine, where the
        // notifications took longer to arrive.
        await WaitUntilAsync(() => wakeUps.CountAfter(afterConnect) >= 1);
        await WaitUntilQuietAsync(() => wakeUps.Count);

        // One wake-up per notification, a debounce apart, would be dozens (33 before the burst was drained).
        output.WriteLine($"{wakeUps.CountAfter(afterConnect)} wake-up(s) for 100 notifications");
        wakeUps.CountAfter(afterConnect).ShouldBeInRange(1, 9);
    }

    private static async Task WaitUntilQuietAsync(Func<int> count)
    {
        var deadline = TimeProvider.System.GetUtcNow() + TimeSpan.FromSeconds(10);
        var last = count();
        while (true)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            var now = count();
            if (now == last)
                return;
            if (TimeProvider.System.GetUtcNow() > deadline)
                throw new TimeoutException("The outbox kept being woken.");
            last = now;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = TimeProvider.System.GetUtcNow() + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (TimeProvider.System.GetUtcNow() > deadline)
                throw new TimeoutException("The condition wasn't met in time.");
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
