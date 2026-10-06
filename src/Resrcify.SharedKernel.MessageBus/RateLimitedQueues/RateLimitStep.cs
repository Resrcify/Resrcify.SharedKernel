using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Rebus.Pipeline;
using Resrcify.SharedKernel.MessageBus.Diagnostics;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// Rate-limits message handling on one bus with the queue's limiter. A message waits for a lease
/// instead of being rejected, so a burst drains at the limiter's rate.
/// </summary>
/// <remarks>
/// A waiting message is unacknowledged on this instance, so prefetch bounds how much work one
/// instance can hold. Keep prefetch near the per-second rate. When the queue stops consuming, a waiting message
/// stops waiting and goes back to the queue.
/// </remarks>
internal sealed class RateLimitStep(RateLimiter limiter, string queueName, TimeProvider time, CancellationToken consuming)
    : IIncomingStep
{
    public async Task Process(IncomingStepContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var waitStarted = time.GetTimestamp();
        using var lease = await limiter.AcquireAsync(1, consuming);
        if (!lease.IsAcquired)
            throw new InvalidOperationException("The rate limiter's queue is full.");
        MessageBusDiagnostics.RecordRateLimitWait(queueName, time.GetElapsedTime(waitStarted));
        await next();
    }
}
