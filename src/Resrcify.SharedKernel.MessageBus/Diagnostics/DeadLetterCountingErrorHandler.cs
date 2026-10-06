using System;
using System.Threading.Tasks;
using Rebus.Messages;
using Rebus.Retry;
using Rebus.Transport;

namespace Resrcify.SharedKernel.MessageBus.Diagnostics;

/// <summary>
/// Counts each message that kept failing (<c>messagebus.messages.dead_lettered</c>), then lets Rebus' error handler
/// move it to the error queue as usual.
/// </summary>
internal sealed class DeadLetterCountingErrorHandler(IErrorHandler inner, string queueName) : IErrorHandler
{
    public async Task HandlePoisonMessage(TransportMessage transportMessage, ITransactionContext transactionContext, ExceptionInfo exception)
    {
        ArgumentNullException.ThrowIfNull(transportMessage);
        transportMessage.Headers.TryGetValue(Headers.Type, out var messageType);
        await inner.HandlePoisonMessage(transportMessage, transactionContext, exception);
        MessageBusDiagnostics.RecordDeadLettered(queueName, messageType ?? "unknown");
    }
}
