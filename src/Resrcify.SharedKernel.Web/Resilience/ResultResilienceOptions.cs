using System;
using System.Collections.Generic;

namespace Resrcify.SharedKernel.Web.Resilience;

/// <summary>
/// How <c>AddResultResilience</c> retries, times out and breaks the circuit. Which responses are retried follows the
/// result pattern: a status whose <c>ErrorType</c> (as <c>HttpResponseMessage.ToResultAsync</c> reads it) is
/// transient (<c>Failure</c>, <c>ExternalFailure</c>, <c>Timeout</c>, <c>RateLimit</c>: a 5xx, a 408 or a 429), plus
/// network errors and attempts that timed out. Any other 4xx (400, 404, 409, 422, …) is an answer, not retried, unless
/// listed in <see cref="AlsoRetry"/>.
/// </summary>
public sealed class ResultResilienceOptions
{
    /// <summary>Retries after the first attempt (default 3); 0 turns retrying off.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>The first retry's delay (default 1 s); each next one doubles it, up to <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The longest computed delay between two attempts (default 30 s). A <c>Retry-After</c> isn't capped by it; one at
    /// least as long as what is left of <see cref="TotalTimeout"/> isn't waited for, the response is returned instead.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Spread the delays randomly (default on), so the clients of a failing service don't retry in step.</summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>One attempt's longest time (default 10 s); an attempt that takes longer is cancelled and retried.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The longest time for the whole call, retries and delays included (default 30 s).</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Break the circuit (default on): when <see cref="CircuitBreakerFailureRatio"/> of at least
    /// <see cref="CircuitBreakerMinimumThroughput"/> calls in <see cref="CircuitBreakerSamplingDuration"/> fail
    /// transiently, calls fail at once (<c>BrokenCircuitException</c>) for <see cref="CircuitBreakerBreakDuration"/>.
    /// </summary>
    public bool CircuitBreaker { get; set; } = true;

    public double CircuitBreakerFailureRatio { get; set; } = 0.1;

    public int CircuitBreakerMinimumThroughput { get; set; } = 100;

    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Statuses retried although their error type isn't transient, e.g. 404 for an upstream that answers "not found"
    /// until it has caught up. They don't count against the circuit.
    /// </summary>
    public ISet<int> AlsoRetry { get; } = new HashSet<int>();

    /// <summary>Statuses never retried (nor counted against the circuit), although their error type is transient.</summary>
    public ISet<int> NeverRetry { get; } = new HashSet<int>();
}
