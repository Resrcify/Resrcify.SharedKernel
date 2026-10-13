using System.Threading.RateLimiting;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

namespace Resrcify.SharedKernel.MessageBus.Abstractions;

/// <summary>
/// Creates the limiter a rate-limited queue applies before handling each request, per service instance.
/// Set it on <see cref="RateLimitedQueueOptions.RateLimiter"/>; the default,
/// <see cref="TokenBucketRateLimiterStrategy"/>, allows <see cref="RateLimitedQueueOptions.PerSecond"/>
/// requests a second with a burst of <see cref="RateLimitedQueueOptions.Burst"/>.
/// </summary>
/// <remarks>
/// A request waits for a lease rather than being rejected, so return a limiter that queues (a
/// <see cref="RateLimiter"/> whose queue limit is above zero). A limiter is created each time the queue's bus
/// starts, and disposed when it stops.
/// </remarks>
public interface IRateLimiterStrategy
{
    RateLimiter CreateLimiter(string queueName, RateLimitedQueueOptions options);
}
