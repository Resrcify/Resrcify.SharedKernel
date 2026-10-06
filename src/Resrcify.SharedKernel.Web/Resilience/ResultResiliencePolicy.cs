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
    /// <summary>
    /// A network error, a timed-out attempt, or a response whose status is not in <c>NeverRetry</c> and is in
    /// <c>AlsoRetry</c> or a failure of a transient error type. The caller's cancellation is never retried.
    /// </summary>
    public bool ShouldRetry(
        Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Result is not { } response)
            return IsTransientException(outcome.Exception);

        var status = (int)response.StatusCode;
        if (options.NeverRetry.Contains(status))
            return false;

        return options.AlsoRetry.Contains(status) || IsTransientFailure(response);
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

    private static bool IsTransientException(
        Exception? exception)
        => exception is HttpRequestException or TimeoutRejectedException;

    private static TimeSpan Clamp(
        TimeSpan delay)
        => delay > TimeSpan.Zero
            ? delay
            : TimeSpan.Zero;
}
