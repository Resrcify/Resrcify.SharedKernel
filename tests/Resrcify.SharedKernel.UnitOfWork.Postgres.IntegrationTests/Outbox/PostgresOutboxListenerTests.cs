using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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

        var handled = host.Tracker.HandledAsync(shard.Id);
        (await Task.WhenAny(handled, Task.Delay(TimeSpan.FromSeconds(2)))).ShouldNotBe(handled);
    }
}
