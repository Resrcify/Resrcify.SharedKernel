using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.MessageBus.PublishSubscribe;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.PublishSubscribe;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class SubscriptionOptionsTests
{
    [Fact]
    public async Task RunAsync_ShouldHandleOneAtATimeInArrivalOrder_WhenEventsShareAPartitionKey()
    {
        var options = new SubscriptionOptions<Payout>().HandleInPartitions(payout => payout.ShardId);
        var tracker = new ConcurrencyTracker();

        await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            options.RunAsync(new Payout(1, i), () => tracker.RunAsync(i))));

        tracker.MostAtOnce.ShouldBe(1);
        tracker.Order.ShouldBe(Enumerable.Range(0, 10));
    }

    [Fact]
    public async Task RunAsync_ShouldHandleConcurrently_WhenEventsHaveDifferentKeysInDifferentPartitions()
    {
        var options = new SubscriptionOptions<Payout>().HandleInPartitions(payout => payout.ShardId, partitions: 4);
        var tracker = new ConcurrencyTracker();

        // Keys 0..3 hash to the four partitions (an int's hash is itself).
        await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            options.RunAsync(new Payout(i, i), () => tracker.RunAsync(i))));

        tracker.MostAtOnce.ShouldBe(4);
    }

    [Fact]
    public async Task RunAsync_ShouldHandleEverythingOneAtATime_WhenSetToOneAtATime()
    {
        var options = new SubscriptionOptions<Payout>().HandleOneAtATime();
        var tracker = new ConcurrencyTracker();

        await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            options.RunAsync(new Payout(i, i), () => tracker.RunAsync(i))));

        tracker.MostAtOnce.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ShouldKeepThePartitionUsable_WhenAHandlerThrows()
    {
        var options = new SubscriptionOptions<Payout>().HandleOneAtATime();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            options.RunAsync(new Payout(1, 1), () => throw new InvalidOperationException("Fails.")));
        var ran = false;
        await options.RunAsync(new Payout(1, 2), () =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        ran.ShouldBeTrue();
    }

    internal sealed record Payout(int ShardId, int Sequence);

    private sealed class ConcurrencyTracker
    {
        private int _running;
        private int _mostAtOnce;

        public ConcurrentQueue<int> Order { get; } = new();

        public int MostAtOnce => _mostAtOnce;

        public async Task RunAsync(int sequence)
        {
            var running = Interlocked.Increment(ref _running);
            InterlockedMax(ref _mostAtOnce, running);
            Order.Enqueue(sequence);
            await Task.Delay(30);
            Interlocked.Decrement(ref _running);
        }

        private static void InterlockedMax(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var previous = Interlocked.CompareExchange(ref target, value, current);
                if (previous == current)
                    return;
                current = previous;
            }
        }
    }
}
