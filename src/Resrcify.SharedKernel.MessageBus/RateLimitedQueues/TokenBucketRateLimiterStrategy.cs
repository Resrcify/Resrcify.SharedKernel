using System;
using System.Threading.RateLimiting;
using Resrcify.SharedKernel.MessageBus.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// The default limiter: a token bucket refilled at <see cref="RateLimitedQueueOptions.PerSecond"/>, holding up to
/// <see cref="RateLimitedQueueOptions.Burst"/>. Requests beyond that wait for a token, so a burst drains at the
/// configured rate. In any one second at most <c>PerSecond + Burst</c> go through (or <c>PerSecond</c> plus one
/// refill step, if that is larger than <c>Burst</c>).
/// </summary>
public sealed class TokenBucketRateLimiterStrategy : IRateLimiterStrategy
{
    /// <summary>
    /// The shortest refill step. Timers are coarse (about 15.6 ms on Windows), so refilling one token every
    /// 1/PerSecond seconds undershoots at high rates (11 ms steps at 90/s gave ~70/s); refilling several tokens
    /// per step of at least this long keeps the rate exact.
    /// </summary>
    private static readonly TimeSpan MinimumRefillStep = TimeSpan.FromMilliseconds(50);

    public static TokenBucketRateLimiterStrategy Instance { get; } = new();

    public RateLimiter CreateLimiter(string queueName, RateLimitedQueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var tokensPerStep = Math.Max(1, (int)Math.Ceiling(options.PerSecond * MinimumRefillStep.TotalSeconds));
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            // At least one step's worth, or the refill would overflow the bucket and the rate drop.
            TokenLimit = Math.Max(options.Burst, tokensPerStep),
            TokensPerPeriod = tokensPerStep,
            ReplenishmentPeriod = TimeSpan.FromSeconds((double)tokensPerStep / options.PerSecond),
            QueueLimit = 100_000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    }
}
