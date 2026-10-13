using System;
using System.Net.Http;
using Polly;
using Polly.Timeout;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Extensions;

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
    /// A network error, a timed-out attempt, or a response whose status is not in <c>NeverRetry</c> and is in
    /// <c>AlsoRetry</c> or a failure of a transient error type. The caller's cancellation is never retried, nor a
    /// response whose <c>Retry-After</c> asks for at least what is left of the total timeout (known from
    /// <paramref name="context"/>): waiting for it would only end in a timeout, so the caller gets the response now.
    /// </summary>
    public bool ShouldRetry(
        Outcome<HttpResponseMessage> outcome,
        ResilienceContext? context = null)
    {
        if (outcome.Result is not { } response)
            return IsTransientException(outcome.Exception);

        var status = (int)response.StatusCode;
        if (options.NeverRetry.Contains(status))
            return false;

        if (!options.AlsoRetry.Contains(status) && !IsTransientFailure(response))
            return false;

        return RetryAfter(response) is not { } wait || wait < TimeLeft(context);
    }

    /// <summary>What the circuit counts as a failure: what is retried, but not the statuses of <c>AlsoRetry</c>.</summary>
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

    private bool IsTransientFailure(
        HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode || status < 400 || options.NeverRetry.Contains(status))
            return false;

        return HttpResultExtensions.GetErrorType(status).IsTransient();
    }

    private TimeSpan TimeLeft(
        ResilienceContext? context)
    {
        if (context is null
            || options.TotalTimeout <= TimeSpan.Zero
            || !context.Properties.TryGetValue(CallStarted, out var started))
            return TimeSpan.MaxValue;

        return options.TotalTimeout - timeProvider.GetElapsedTime(started);
    }

    private static bool IsTransientException(
        Exception? exception)
        => exception is HttpRequestException or TimeoutRejectedException;

    private static TimeSpan Clamp(
        TimeSpan delay)
        => delay > TimeSpan.Zero
            ? delay
            : TimeSpan.Zero;
}
