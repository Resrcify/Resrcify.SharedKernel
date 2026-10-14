using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resrcify.SharedKernel.Web.Resilience;

/// <summary>
/// How <c>AddResultResilience</c> retries, times out and breaks the circuit. Which responses are retried follows the
/// result pattern: a status whose <c>ErrorType</c> (as <c>HttpResponseMessage.ToResultAsync</c> reads it) is
/// transient (<c>Failure</c>, <c>ExternalFailure</c>, <c>Timeout</c>, <c>RateLimit</c>: a 5xx, a 408 or a 429), plus
/// network errors and attempts that timed out. Any other 4xx (400, 404, 409, 422, …) is an answer, not retried, unless
/// listed in <see cref="AlsoRetry"/>. <see cref="RetryOnly"/> replaces that rule with a list of its own.
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

    /// <summary>
    /// The only statuses retried, when set (default <see langword="null"/>: the error type decides, with
    /// <see cref="AlsoRetry"/> and <see cref="NeverRetry"/>). Network errors (<c>HttpRequestException</c>) and attempts
    /// that timed out are still retried; any status not listed is returned at once and doesn't count against the
    /// circuit. A listed status counts against the circuit when its error type is transient, as without the list. An
    /// empty set retries no status at all. E.g. <see cref="SafeToRetryStatuses"/> for a service that retries its own
    /// failures, or <c>new HashSet&lt;int&gt;(ResultResilienceOptions.SafeToRetryStatuses) { 404 }</c> for more.
    /// </summary>
    /// <remarks>
    /// It lists every status retried, so it can't be combined with <see cref="AlsoRetry"/> or <see cref="NeverRetry"/>,
    /// and holds error statuses (400–599) only: <c>AddResultResilience</c> throws an
    /// <see cref="InvalidOperationException"/> as the service registers it otherwise, rather than pick one silently.
    /// </remarks>
    public IReadOnlySet<int>? RetryOnly { get; set; }

    /// <summary>
    /// Throw a timeout (<c>TimeoutRejectedException</c>, an attempt's or the whole call's) and an open circuit
    /// (<c>BrokenCircuitException</c>) to the caller, as 4.0.0 did. Off by default: the caller gets a response made up
    /// in their place instead, which <c>ToResultAsync</c> reads as <see cref="ResultResilienceErrors.Timeout"/> (504) or
    /// <see cref="ResultResilienceErrors.CircuitOpen"/> (502), like any other failed answer (see
    /// <see cref="ResultResilienceErrors"/>). The caller's own cancellation throws <c>OperationCanceledException</c>
    /// either way.
    /// </summary>
    public bool ThrowTimeoutsAndOpenCircuits { get; set; }

    /// <summary>
    /// 408, 429 and 503, for <see cref="RetryOnly"/>: what is safe to retry when the service called retries its own
    /// failures, so that any other 5xx is its final answer. The request didn't arrive in time (408), the service asks to
    /// come back later (429, after its <c>Retry-After</c>) or isn't up yet (503, e.g. restarting).
    /// </summary>
    /// <example>
    /// <code>
    /// client.AddResultResilience(resilience => resilience.RetryOnly = ResultResilienceOptions.SafeToRetryStatuses);
    /// </code>
    /// </example>
    public static IReadOnlySet<int> SafeToRetryStatuses { get; } = new[]
    {
        StatusCodes.RequestTimeout,
        StatusCodes.TooManyRequests,
        StatusCodes.ServiceUnavailable,
    }.ToFrozenSet();

    /// <summary>
    /// Throws on a contradictory configuration, as the service registers the client; takes a copy of
    /// <see cref="RetryOnly"/>, so that changing the set given afterwards changes nothing.
    /// </summary>
    internal void Validate()
    {
        if (RetryOnly is not { } retryOnly)
            return;

        if (AlsoRetry.Count > 0 || NeverRetry.Count > 0)
            throw new InvalidOperationException(
                "ResultResilienceOptions.RetryOnly lists every status retried, so it can't be combined with AlsoRetry "
                + "or NeverRetry: add the statuses to RetryOnly, or leave them out of it.");

        var notErrorStatuses = retryOnly
            .Where(status => status is < 400 or > 599)
            .Order()
            .Select(status => status.ToString(CultureInfo.InvariantCulture))
            .ToList();
        if (notErrorStatuses.Count > 0)
            throw new InvalidOperationException(
                "ResultResilienceOptions.RetryOnly holds error statuses (400-599) only, not "
                + string.Join(", ", notErrorStatuses) + ".");

        RetryOnly = retryOnly.ToFrozenSet();
    }

    private static class StatusCodes
    {
        public const int RequestTimeout = 408;
        public const int TooManyRequests = 429;
        public const int ServiceUnavailable = 503;
    }
}
