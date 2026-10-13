using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rebus.Messages;
using Rebus.Retry;
using Rebus.Transport;
using Resrcify.SharedKernel.MessageBus.Diagnostics;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// What happens to a scatter request the bus itself couldn't handle on a rate-limited queue (unreadable, or cancelled on
/// every try; a responder's failures are answered instead): it is logged and dropped, not moved to the <c>error</c>
/// queue. Its requester waits a limited time and counts it as unanswered either way, and its time-to-live would expire
/// it soon anyway, so a copy in the error queue would only pile up.
/// </summary>
internal sealed partial class DropFailedRequestErrorHandler(string queueName, ILogger logger) : IErrorHandler
{
    public Task HandlePoisonMessage(TransportMessage transportMessage, ITransactionContext transactionContext, ExceptionInfo exception)
    {
        ArgumentNullException.ThrowIfNull(transportMessage);
        ArgumentNullException.ThrowIfNull(exception);
        transportMessage.Headers.TryGetValue(Headers.MessageId, out var messageId);
        LogDropped(queueName, messageId, exception.Message);
        MessageBusDiagnostics.RecordDropped(queueName);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped request {MessageId} on {Queue} after it kept failing: {Error}")]
    private partial void LogDropped(string queue, string? messageId, string error);
}
