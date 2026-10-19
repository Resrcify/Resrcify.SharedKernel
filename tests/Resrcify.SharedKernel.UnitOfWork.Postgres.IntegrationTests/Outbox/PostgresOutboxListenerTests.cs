using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Resrcify.SharedKernel.IntegrationTesting.Waiting;
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

    // A debounce no other timer of the host uses, so DebounceClock tells the listener's apart.
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(3_517);

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

    /// <summary>
    /// A burst sent while the listener's debounce is pending wakes the outbox once. The host's clock is held
    /// (<see cref="DebounceClock"/>): the debounce ends when the test ends it, after the whole burst was sent, so the
    /// outcome doesn't depend on how fast the machine delivers the notifications (on real time, a loaded machine spread
    /// them over many debounces and woke the outbox some 20 times).
    /// </summary>
    [Fact]
    public async Task ABurstOfNotifications_ShouldWakeTheOutboxOnce()
    {
        var clock = new DebounceClock(Debounce);
        using var wakeUps = new WakeUpLog();
        await using var host = await TestHost.CreateAsync(
            postgres,
            db => db.WithOutbox().WithOutboxWakeUp(wakeUp => wakeUp.Debounce = Debounce),
            services => services
                .AddSingleton<TimeProvider>(clock)
                .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(wakeUps)));
        await host.Host.StartAsync();
        await HangGuard.AwaitAsync(wakeUps.ReachedAsync(1), "the wake-up the listener's connect gives");

        await host.NotifyAsync(times: 1);
        await HangGuard.AwaitAsync(clock.DebouncingAsync(1), "the listener's debounce of the first notification");
        await host.NotifyAsync(times: 99);
        clock.EndDebounce();
        await HangGuard.AwaitAsync(wakeUps.ReachedAsync(2), "the burst's wake-up");

        // The connect's and the burst's. Another would need another debounce to end, and the clock stands still.
        wakeUps.Count.ShouldBe(2);
        output.WriteLine("2 wake-ups: the connect's, and one for the burst of 100 notifications");
    }
}
