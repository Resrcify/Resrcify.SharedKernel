namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>A queue this instance consumes, for the health check and the <c>messagebus.queue.consuming</c> gauge.</summary>
internal interface IQueueConsumer
{
    string QueueName { get; }

    /// <summary>False while the queue has stepped aside (its health check is unhealthy) or isn't started.</summary>
    bool IsConsuming { get; }
}
