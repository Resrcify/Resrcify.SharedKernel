using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Messages;
using Rebus.Transport;
using Resrcify.SharedKernel.MessageBus.PublishSubscribe;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.PublishSubscribe;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ReceiveOrderTransportTests
{
    [Fact]
    public async Task Receive_ShouldNumberEveryMessageInTheOrderItWasDelivered_WhenManyReceivesWaitAtOnce()
    {
        var queue = new DeliveringTransport();
        using var transport = new ReceiveOrderTransport(queue, new ReceiveOrder());

        // As the bus' worker does with MaxParallelism 20: twenty receives waiting at once.
        var received = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => ReceiveAsync(transport))));

        queue.MostAtOnce.ShouldBe(1);
        foreach (var (delivered, number) in received)
            number.ShouldBe(delivered);
    }

    private static async Task<(long Delivered, long Number)> ReceiveAsync(ReceiveOrderTransport transport)
    {
        using var scope = new RebusTransactionScope();
        var message = await transport.Receive(scope.TransactionContext, CancellationToken.None);
        var delivered = long.Parse(message!.Headers["delivered"], CultureInfo.InvariantCulture);
        return (delivered, (long)scope.TransactionContext.Items[ReceiveOrder.ItemKey]);
    }

    /// <summary>Hands out messages in delivery order, each after a wait (a receive resumes on its own thread).</summary>
    private sealed class DeliveringTransport : ITransport
    {
        private long _delivered;
        private int _receiving;
        private int _mostAtOnce;

        public int MostAtOnce => Volatile.Read(ref _mostAtOnce);

        public string Address => "subscriber";

        public void CreateQueue(string address)
        {
        }

        public Task Send(string destinationAddress, TransportMessage message, ITransactionContext context)
            => Task.CompletedTask;

        public async Task<TransportMessage?> Receive(ITransactionContext context, CancellationToken cancellationToken)
        {
            var atOnce = Interlocked.Increment(ref _receiving);
            InterlockedMax(atOnce);
            var delivered = Interlocked.Increment(ref _delivered);
            await Task.Delay(4 - (int)(delivered % 4), cancellationToken);   // later deliveries can resume first
            Interlocked.Decrement(ref _receiving);
            return new TransportMessage(
                new Dictionary<string, string> { ["delivered"] = delivered.ToString(CultureInfo.InvariantCulture) },
                []);
        }

        private void InterlockedMax(int value)
        {
            var current = Volatile.Read(ref _mostAtOnce);
            while (value > current)
            {
                var previous = Interlocked.CompareExchange(ref _mostAtOnce, value, current);
                if (previous == current)
                    return;
                current = previous;
            }
        }
    }
}
