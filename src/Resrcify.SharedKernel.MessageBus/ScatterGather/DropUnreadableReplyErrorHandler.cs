using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rebus.Messages;
using Rebus.Retry;
using Rebus.Transport;
using Resrcify.SharedKernel.MessageBus.Diagnostics;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>
/// What happens to a reply this instance can't read (e.g. of a type it doesn't receive): it is logged, counted
/// (<c>messagebus.scatter_gather.replies</c>, outcome <c>unreadable</c>) and dropped, not moved to Rebus' shared
/// <c>error</c> queue. Nobody could use it there, and its batch ends at its timeout with the item unanswered anyway.
/// </summary>
internal sealed partial class DropUnreadableReplyErrorHandler(ILogger logger) : IErrorHandler
{
    public Task HandlePoisonMessage(TransportMessage transportMessage, ITransactionContext transactionContext, ExceptionInfo exception)
    {
        ArgumentNullException.ThrowIfNull(transportMessage);
        ArgumentNullException.ThrowIfNull(exception);
        transportMessage.Headers.TryGetValue(Headers.Type, out var messageType);
        LogDropped(messageType, exception.Message);
        MessageBusDiagnostics.RecordReply(ReplyOutcome.Unreadable);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped a scatter-gather reply ({MessageType}) that couldn't be read: {Error}")]
    private partial void LogDropped(string? messageType, string error);
}
