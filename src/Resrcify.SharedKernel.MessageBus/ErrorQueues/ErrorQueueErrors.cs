using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.ErrorQueues;

/// <summary>The errors the error queue (<see cref="Abstractions.MessageBus.IErrorQueue"/>) reports.</summary>
public static class ErrorQueueErrors
{
    /// <summary>The service has no input queue, so it subscribes to nothing and has no error queue.</summary>
    public static Error None
        => new("ErrorQueue.None", "This service has no error queue: it has no input queue.", ErrorType.NotFound);

    /// <summary>No message with the ID is in the error queue.</summary>
    public static Error MessageNotFound(string messageId)
        => new("ErrorQueue.MessageNotFound", $"No message {messageId} in the error queue.", ErrorType.NotFound);

    /// <summary>The message couldn't be moved back, and stays in the error queue.</summary>
    public static Error NotMoved(string messageId, string reason)
        => new("ErrorQueue.NotMoved", $"Message {messageId} wasn't moved back ({reason}); it stays in the error queue.", ErrorType.Conflict);

    /// <summary>The broker couldn't be reached.</summary>
    public static Error Unavailable(string reason)
        => new("ErrorQueue.Unavailable", $"The error queue can't be reached: {reason}", ErrorType.ExternalFailure);
}
