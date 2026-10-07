namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>A rate-limited queue's name, registered once per queue so a second queue of the same name is refused.</summary>
internal sealed record RateLimitedQueueName(string Name);
