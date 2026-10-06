using System;
using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>Limits for one rate-limited queue, per service instance.</summary>
public sealed class RateLimitedQueueOptions
{
    /// <summary>Requests handled per second on this instance. Each added instance adds the same again.</summary>
    public int PerSecond { get; set; } = 10;

    /// <summary>How many requests may run back-to-back before the rate applies.</summary>
    public int Burst { get; set; } = 1;

    /// <summary>
    /// Messages this instance takes off the queue ahead of handling them. Defaults to one second of
    /// work: anything prefetched is held by this instance until it handles it or shuts its bus down.
    /// </summary>
    public int? Prefetch { get; set; }

    /// <summary>
    /// Health-check tag that gates consumption. While a check with this tag is unhealthy, the queue's
    /// bus is shut down, which hands its prefetched messages back to the other instances; it is
    /// started again once healthy. <see langword="null"/> disables gating.
    /// </summary>
    public string? HealthCheckTag { get; set; }

    /// <summary>How often the health gate is evaluated.</summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Creates the queue's limiter. Defaults to <see cref="TokenBucketRateLimiterStrategy"/>, which uses
    /// <see cref="PerSecond"/> and <see cref="Burst"/>; a custom strategy may use them or not.
    /// </summary>
    public IRateLimiterStrategy RateLimiter { get; set; } = TokenBucketRateLimiterStrategy.Instance;

    /// <summary>
    /// Whether a failed result is answered at once (<see langword="true"/>), or first sent back to the queue for
    /// another try (<see langword="false"/>), up to the bus' delivery attempts, after which it is answered anyway.
    /// Default: <see cref="AnswerFailureWhenItsTheRequestsFault"/>.
    /// </summary>
    public Func<IReadOnlyList<Error>, bool> AnswerFailure { get; set; } = AnswerFailureWhenItsTheRequestsFault;

    /// <summary>
    /// Answers a failure when every error is about the request itself (<see cref="ErrorType.NotFound"/>,
    /// <see cref="ErrorType.Validation"/>, <see cref="ErrorType.Conflict"/>, <see cref="ErrorType.Unauthorized"/>,
    /// <see cref="ErrorType.Forbidden"/>): another attempt would fail the same way. Anything else (a failure, an
    /// upstream's failure, a timeout, a rate limit) may pass, so it is retried.
    /// </summary>
    public static bool AnswerFailureWhenItsTheRequestsFault(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return MessageFailures.AreTheMessagesFault(errors);
    }

    internal int EffectivePrefetch => Prefetch ?? Math.Max(PerSecond, Burst);
}
