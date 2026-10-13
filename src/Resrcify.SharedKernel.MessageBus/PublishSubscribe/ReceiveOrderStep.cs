using System;
using System.Threading.Tasks;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Transport;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>
/// Admits every message that isn't an event handled in publish order as soon as it is read, so it never holds up the
/// ordered events behind it. An ordered event is admitted by its dispatcher, once it has joined its partition.
/// </summary>
internal sealed class ReceiveOrderStep(ReceiveOrder order, MessageBusSettings settings)
    : IIncomingStep
{
    public async Task Process(IncomingStepContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var transaction = context.Load<ITransactionContext>();
        if (transaction.Items.TryGetValue(ReceiveOrder.ItemKey, out var number) && number is long value)
        {
            var message = context.Load<Message>();
            if (message?.Body is not { } body || !settings.HandlesInPublishOrder(body.GetType()))
                order.Admit(value);
        }
        await next();
    }
}
