using System;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Messages;
using Rebus.Transport;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>
/// Numbers each message as the bus receives it (<see cref="ReceiveOrder"/>), in the order the transport hands them out,
/// which is the queue's order, the order the events were published in.
/// </summary>
/// <remarks>
/// The worker keeps up to <c>MaxParallelism</c> receives waiting at once; each resumes on a thread of its own, so a
/// number taken after the receive returned would follow thread scheduling, not delivery. Receiving and numbering are
/// done together, one receive at a time; the messages are still handled in parallel.
/// </remarks>
internal sealed class ReceiveOrderTransport(ITransport transport, ReceiveOrder order)
    : ITransport, IDisposable
{
    private readonly SemaphoreSlim _receiving = new(1, 1);

    // Only its own gate: Rebus disposes the transport it decorates itself.
    public void Dispose()
        => _receiving.Dispose();

    public string Address
        => transport.Address;

    public void CreateQueue(string address)
        => transport.CreateQueue(address);

    public Task Send(string destinationAddress, TransportMessage message, ITransactionContext context)
        => transport.Send(destinationAddress, message, context);

    public async Task<TransportMessage?> Receive(ITransactionContext context, CancellationToken cancellationToken)
    {
        await _receiving.WaitAsync(cancellationToken);
        try
        {
            var message = await transport.Receive(context, cancellationToken);
            if (message is not null)
                order.Stamp(context);
            return message;
        }
        finally
        {
            _receiving.Release();
        }
    }
}
