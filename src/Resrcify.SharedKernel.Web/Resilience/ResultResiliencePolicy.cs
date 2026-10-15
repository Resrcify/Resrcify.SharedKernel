using System;
using System.Net.Http;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Polly.RateLimiting;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Web.Resilience;

/// <summary>Which outcomes <c>AddResultResilience</c> retries and counts against the circuit, and how long a <c>Retry-After</c> asks to wait.</summary>
internal sealed class ResultResiliencePolicy(
    ResultResilienceOptions options,
    TimeProvider timeProvider)
{
    private const string CallStartedKey = "Resrcify.ResultResilience.CallStarted";

    /// <summary>
    /// When the call started (a <see cref="TimeProvider"/> timestamp), set by <see cref="StartCall"/>. A property, not
    /// a field: a field of a Polly type would need Polly to load this class (see WebAssemblyTests).
    /// </summary>
    public static ResiliencePropertyKey<long> CallStarted => new(CallStartedKey);

    /// <summary>
    /// The total timeout's generator, so called once as the call starts: notes the start on the context, for
    /// <see cref="ShouldRetry"/> to tell how much of the total timeout is left.
    /// </summary>
    public TimeSpan StartCall(
        ResilienceContext context)
    {
        context.Properties.Set(CallStarted, timeProvider.GetTimestamp());
        return options.TotalTimeout;
    }

    /// <summary>
    /// A transient exception (a network error, a timed-out attempt: see <see cref="IsTransientException"/>), or a
    /// response whose status is retried: in <c>RetryOnly</c> when it is set;
    /// otherwise not in <c>NeverRetry</c>, and in <c>AlsoRetry</c> or a failure of a transient error type. The caller's
    /// cancellation is never retried, nor a response whose <c>Retry-After</c> asks for at least what is left of the
    /// total timeout (known from <paramref name="context"/>): waiting for it would only end in a timeout, so the caller
    /// gets the response now.
    /// </summary>
    public bool ShouldRetry(
        Outcome<HttpResponseMessage> outcome,
        ResilienceContext? context = null)
    {
        if (outcome.Result is not { } response)
            return IsTransientException(outcome.Exception);

        if (!IsRetriedStatus(response))
            return false;

        return RetryAfter(response) is not { } wait || wait < TimeLeft(context);
    }

    /// <summary>
    /// What the circuit counts as a failure: what is retried, but not a status whose error type isn't transient (those
    /// of <c>AlsoRetry</c>, or a 4xx in <c>RetryOnly</c>).
    /// </summary>
    public bool CountsAgainstTheCircuit(
        Outcome<HttpResponseMessage> outcome)
        => outcome.Result is { } response
            ? IsTransientFailure(response)
            : IsTransientException(outcome.Exception);

    /// <summary>
    /// The wait a response's <c>Retry-After</c> asks for (a delay, or a date on the <see cref="TimeProvider"/>'s
    /// clock); <see langword="null"/> without one, which leaves the computed delay.
    /// </summary>
    public TimeSpan? RetryAfter(
        HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return Clamp(delta);

        if (retryAfter?.Date is { } date)
            return Clamp(date - timeProvider.GetUtcNow());

        return null;
    }

    /// <summary>
    /// Whether the pipeline answers <paramref name="exception"/> in the service's place (see
    /// <see cref="ResultResilienceErrors"/>): a timeout, an open circuit, or no answer at all after the retries (an
    /// <see cref="HttpRequestException"/> without a status), unless they are thrown. Never the caller's cancellation (an
    /// <see cref="OperationCanceledException"/>, which none of them is).
    /// </summary>
    public bool AnswersInPlaceOf(
        Exception? exception)
        => !options.ThrowTimeoutsAndOpenCircuits
            && exception is TimeoutRejectedException or BrokenCircuitException or HttpRequestException { StatusCode: null };

    private bool IsRetriedStatus(
        HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (options.RetryOnly is { } retryOnly)
            return retryOnly.Contains(status);

        if (options.NeverRetry.Contains(status))
            return false;

        return options.AlsoRetry.Contains(status) || IsTransientFailure(response);
    }

    private bool IsTransientFailure(
        HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode || status < 400 || IsLeftUnretried(status))
            return false;

        return ErrorTypes.FromHttpStatus(status).IsTransient();
    }

    // Left out of RetryOnly when it is set, otherwise listed in NeverRetry.
    private bool IsLeftUnretried(
        int status)
        => options.RetryOnly is { } retryOnly
            ? !retryOnly.Contains(status)
            : options.NeverRetry.Contains(status);

    private TimeSpan TimeLeft(
        ResilienceContext? context)
    {
        if (context is null
            || options.TotalTimeout <= TimeSpan.Zero
            || !context.Properties.TryGetValue(CallStarted, out var started))
            return TimeSpan.MaxValue;

        return options.TotalTimeout - timeProvider.GetElapsedTime(started);
    }

    /// <summary>
    /// An exception another attempt may get past: an attempt that timed out, or what SharedKernel's rule calls transient
    /// (<see cref="TransientFailures.IsTransient(Exception)"/>: a network error, an <see cref="HttpRequestException"/>
    /// with a transient status, a timeout). Not an open circuit or a rate limiter's refusal (an attempt now is refused
    /// the same way), nor a cancellation (the caller's).
    /// </summary>
    internal static bool IsTransientException(
        Exception? exception)
        => exception switch
        {
            null => false,
            TimeoutRejectedException => true,
            BrokenCircuitException or RateLimiterRejectedException => false,
            OperationCanceledException => false,
            _ => TransientFailures.IsTransient(exception),
        };

    private static TimeSpan Clamp(
        TimeSpan delay)
        => delay > TimeSpan.Zero
            ? delay
            : TimeSpan.Zero;
}
