using System;
using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// Limits for one rate-limited queue, per service instance. Each queue's are the named options of the queue's name
/// (<see cref="NameFor{TRequest}"/>), read when the host starts: <c>AddRateLimitedQueue</c>'s <c>configure</c> gives the
/// defaults, and a service can configure them further with the options pattern after <c>AddMessageBus</c>, e.g.
/// <c>services.AddOptions&lt;RateLimitedQueueOptions&gt;(RateLimitedQueueOptions.NameFor&lt;GuildRequest&gt;()).Configure&lt;GameBudgets&gt;((queue, budgets) =&gt; queue.RateLimiter = budgets.Authenticated)</c>.
/// They are validated when the host starts (<c>PerSecond</c> at least 1, <c>Burst</c> not negative, <c>Prefetch</c> at
/// least 1, a positive <c>HealthCheckInterval</c>, ...), naming the queue and the key.
/// </summary>
public sealed class RateLimitedQueueOptions
{
    /// <summary>
    /// The name of <typeparamref name="TRequest"/>'s queue, which its named options go by: the request's class name, as
    /// <c>AddRateLimitedQueue</c> names it. A queue given its own name (<c>AddRateLimitedQueue(queueName, ...)</c>), or a
    /// request given another wire name with <c>AddMessage&lt;TRequest&gt;(wireName)</c>, goes by that name instead.
    /// </summary>
    /// <example><c>services.AddOptions&lt;RateLimitedQueueOptions&gt;(RateLimitedQueueOptions.NameFor&lt;GuildRequest&gt;())</c></example>
    public static string NameFor<TRequest>()
        where TRequest : class
        => typeof(TRequest).Name;

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
    /// How a request that may pass next time is tried again (sent back to its queue, through the rate limiter) before it
    /// is answered with its failure: <see langword="null"/> (the default) is 0.5 s before the second try, doubling up to
    /// 4 s, with as many tries as the bus' retry strategy delivers a message (5 unless a configuration strategy changes
    /// them). The request is held unacknowledged while it waits, so keep <see cref="RetrySchedule.MaxDelay"/> short.
    /// </summary>
    public RetrySchedule? Retries { get; set; }

    /// <summary>
    /// Whether a failed result is answered at once (<see langword="true"/>), or first sent back to the queue for
    /// another try (<see langword="false"/>), up to <see cref="Retries"/>' attempts, after which it is answered anyway.
    /// Default: <see cref="AnswerFailureWhenItsTheRequestsFault"/>.
    /// </summary>
    public Func<IReadOnlyList<Error>, bool> AnswerFailure { get; set; } = AnswerFailureWhenItsTheRequestsFault;

    /// <summary>
    /// Answers a failure when every error is about the request itself (<see cref="ErrorType.NotFound"/>,
    /// <see cref="ErrorType.Validation"/>, <see cref="ErrorType.Conflict"/>, <see cref="ErrorType.Unauthorized"/>,
    /// <see cref="ErrorType.Forbidden"/>, <see cref="ErrorType.Unprocessable"/>): another attempt would fail the same way. Anything else (a failure, an
    /// upstream's failure, a timeout, a rate limit) may pass, so it is retried.
    /// </summary>
    public static bool AnswerFailureWhenItsTheRequestsFault(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return MessageFailures.AreTheMessagesFault(errors);
    }

    internal int EffectivePrefetch => Prefetch ?? Math.Max(PerSecond, Burst);
}
