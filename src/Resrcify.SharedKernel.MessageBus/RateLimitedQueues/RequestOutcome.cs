namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>How a request on a rate-limited queue ended.</summary>
internal enum RequestOutcome
{
    /// <summary>Answered with a response.</summary>
    Success,

    /// <summary>Answered with the handler's errors.</summary>
    Failure,

    /// <summary>A failure another try may fix (or the responder threw): sent back to the queue for another try.</summary>
    Retried,

    /// <summary>Still failing in a way another try may fix on its last try: answered with its errors.</summary>
    GaveUp,

    /// <summary>Its requester had stopped waiting (its time-to-live passed): not answered.</summary>
    Expired,

    /// <summary>The queue stopped consuming mid-request: the request goes back to the queue.</summary>
    Cancelled,
}
