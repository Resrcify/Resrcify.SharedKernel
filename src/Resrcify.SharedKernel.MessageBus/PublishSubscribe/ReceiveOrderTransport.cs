using System.Threading;
using System.Threading.Tasks;
using Rebus.Messages;
using Rebus.Transport;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>
/// Numbers each message as the bus receives it (<see cref="ReceiveOrder"/>). The bus receives on one worker, so the
/// numbers follow the queue's order, which is the order the events were published in.
/// </summary>
internal sealed class ReceiveOrderTransport(ITransport transport, ReceiveOrder order)
    : ITransport
{
    public string Address
        => transport.Address;

    public void CreateQueue(string address)
        => transport.CreateQueue(address);

    public Task Send(string destinationAddress, TransportMessage message, ITransactionContext context)
        => transport.Send(destinationAddress, message, context);

    public async Task<TransportMessage?> Receive(ITransactionContext context, CancellationToken cancellationToken)
    {
        var message = await transport.Receive(context, cancellationToken);
        if (message is not null)
            order.Stamp(context);
        return message;
    }
}
