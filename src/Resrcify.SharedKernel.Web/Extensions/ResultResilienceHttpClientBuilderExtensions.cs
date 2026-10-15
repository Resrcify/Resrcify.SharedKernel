using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Fallback;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Web.Resilience;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class ResultResilienceHttpClientBuilderExtensions
{
    /// <summary>The pipeline's name (per client).</summary>
    public const string PipelineName = "result-resilience";

    /// <summary>
    /// Retries, times out and breaks the circuit around the client's calls, retrying what the result pattern calls
    /// transient (see <see cref="ResultResilienceOptions"/>): a 5xx, a 408 or a 429 (after its <c>Retry-After</c>), network
    /// errors and timed-out attempts, with exponential, jittered delays. Outermost to innermost: the total timeout,
    /// the retries, the circuit breaker, the attempt timeout. Every wait is on the <see cref="TimeProvider"/>
    /// registered in the container (this registers the system clock when none is), so tests can drive it with a fake one
    /// registered before or after.
    /// </summary>
    /// <remarks>
    /// A <c>Retry-After</c> at least as long as what is left of the total timeout isn't waited for: the caller gets
    /// that response (e.g. a 429) at once, rather than a timeout once the wait has used up the call's time.
    /// The service references <c>Microsoft.Extensions.Http.Resilience</c> itself. After the retries the caller gets the
    /// last response, which <c>ToResultAsync</c> turns into a failure. A timeout (an attempt's, after the retries, or
    /// the whole call's), an open circuit or a service that couldn't be reached after the retries (an
    /// <see cref="HttpRequestException"/> without a status) is answered in the service's place, outermost, with a
    /// response that <c>ToResultAsync</c> reads as <see cref="ResultResilienceErrors.Timeout"/>,
    /// <see cref="ResultResilienceErrors.CircuitOpen"/> or <see cref="ResultResilienceErrors.Unreachable"/>; with
    /// <see cref="ResultResilienceOptions.ThrowTimeoutsAndOpenCircuits"/> they throw instead
    /// (<c>TimeoutRejectedException</c>, <c>BrokenCircuitException</c>, <c>HttpRequestException</c>), as
    /// <c>AddStandardResilienceHandler</c> does. A failure while HttpClient buffers the body of an answer still throws:
    /// it happens after the pipeline returned.
    /// The caller's own cancellation always throws <see cref="OperationCanceledException"/>.
    /// It also adds the Web package's transient-failure rule (<c>AddTransientFailureRule</c>): Polly's rejections
    /// (<c>ExecutionRejectedException</c>: a timeout, a broken or isolated circuit, the rate limiter) are transient to the
    /// outbox, the mediator's logging and the message bus, when they are thrown.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The options contradict each other: <see cref="ResultResilienceOptions.RetryOnly"/> with
    /// <see cref="ResultResilienceOptions.AlsoRetry"/> or <see cref="ResultResilienceOptions.NeverRetry"/>, or a status
    /// outside 400-599 in it.
    /// </exception>
    public static IHttpResiliencePipelineBuilder AddResultResilience(
        this IHttpClientBuilder builder,
        Action<ResultResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new ResultResilienceOptions();
        configure?.Invoke(options);
        options.Validate();
        builder.Services.AddTransientFailureRule<PollyTransientFailureRule>();
        builder.Services.TryAddSingleton(TimeProvider.System);

        return builder.AddResilienceHandler(
            PipelineName,
            (pipeline, context) => Configure(
                pipeline,
                options,
                context.ServiceProvider.GetRequiredService<TimeProvider>()));
    }

    private static void Configure(
        ResiliencePipelineBuilder<HttpResponseMessage> pipeline,
        ResultResilienceOptions options,
        TimeProvider timeProvider)
    {
        var policy = new ResultResiliencePolicy(options, timeProvider);
        pipeline.TimeProvider = timeProvider;

        if (!options.ThrowTimeoutsAndOpenCircuits)
            pipeline.AddFallback(AnswerInPlace(policy));

        pipeline.AddTimeout(new HttpTimeoutStrategyOptions
        {
            Timeout = options.TotalTimeout,
            TimeoutGenerator = arguments => ValueTask.FromResult(policy.StartCall(arguments.Context)),
        });

        if (options.Retries.MaxAttempts > 1)
            pipeline.AddRetry(Retry(options, policy));

        if (options.CircuitBreaker)
            pipeline.AddCircuitBreaker(CircuitBreaker(options, policy));

        pipeline.AddTimeout(new HttpTimeoutStrategyOptions { Timeout = options.AttemptTimeout });
    }

    private static HttpRetryStrategyOptions Retry(
        ResultResilienceOptions options,
        ResultResiliencePolicy policy)
        => new()
        {
            // Attempts in all, the first included; Polly counts the retries after it.
            MaxRetryAttempts = options.Retries.MaxAttempts - 1,
            Delay = options.Retries.FirstDelay,
            MaxDelay = options.Retries.MaxDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = options.Retries.Jitter,
            ShouldHandle = arguments => ValueTask.FromResult(policy.ShouldRetry(arguments.Outcome, arguments.Context)),
            // Its own reading of Retry-After, so a date is measured on the TimeProvider's clock.
            ShouldRetryAfterHeader = false,
            DelayGenerator = arguments => ValueTask.FromResult(policy.RetryAfter(arguments.Outcome.Result)),
        };

    // Outermost, around the total timeout: a timeout or an open circuit, whichever strategy gave up, becomes a response.
    private static FallbackStrategyOptions<HttpResponseMessage> AnswerInPlace(
        ResultResiliencePolicy policy)
        => new()
        {
            Name = "AnswerInPlace",
            ShouldHandle = arguments => ValueTask.FromResult(policy.AnswersInPlaceOf(arguments.Outcome.Exception)),
            FallbackAction = arguments => Outcome.FromResultAsValueTask(ResultResilienceAnswer.For(
                arguments.Outcome.Exception!,
                arguments.Context.GetRequestMessage())),
        };

    private static HttpCircuitBreakerStrategyOptions CircuitBreaker(
        ResultResilienceOptions options,
        ResultResiliencePolicy policy)
        => new()
        {
            FailureRatio = options.CircuitBreakerFailureRatio,
            MinimumThroughput = options.CircuitBreakerMinimumThroughput,
            SamplingDuration = options.CircuitBreakerSamplingDuration,
            BreakDuration = options.CircuitBreakerBreakDuration,
            ShouldHandle = arguments => ValueTask.FromResult(policy.CountsAgainstTheCircuit(arguments.Outcome)),
        };
}
