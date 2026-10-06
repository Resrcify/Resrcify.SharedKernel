using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Resrcify.SharedKernel.Web.Resilience;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class ResultResilienceHttpClientBuilderExtensions
{
    /// <summary>The pipeline's name (per client).</summary>
    public const string PipelineName = "result-resilience";

    /// <summary>
    /// Retries, times out and breaks the circuit around the client's calls, retrying what the result pattern calls
    /// transient (see <see cref="ResultResilienceOptions"/>): a 5xx or a 429 (after its <c>Retry-After</c>), network
    /// errors and timed-out attempts, with exponential, jittered delays. Outermost to innermost: the total timeout,
    /// the retries, the circuit breaker, the attempt timeout. Every wait is on the <see cref="TimeProvider"/>
    /// registered in the container (the system clock otherwise), so tests can drive it with a fake one.
    /// </summary>
    /// <remarks>
    /// The service references <c>Microsoft.Extensions.Http.Resilience</c> itself. After the retries the caller gets the
    /// last response, which <c>ToResultAsync</c> turns into a failure; a timeout or an open circuit throws
    /// (<c>TimeoutRejectedException</c>, <c>BrokenCircuitException</c>), as <c>AddStandardResilienceHandler</c> does.
    /// </remarks>
    public static IHttpResiliencePipelineBuilder AddResultResilience(
        this IHttpClientBuilder builder,
        Action<ResultResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new ResultResilienceOptions();
        configure?.Invoke(options);

        return builder.AddResilienceHandler(
            PipelineName,
            (pipeline, context) => Configure(
                pipeline,
                options,
                context.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System));
    }

    private static void Configure(
        ResiliencePipelineBuilder<HttpResponseMessage> pipeline,
        ResultResilienceOptions options,
        TimeProvider timeProvider)
    {
        var policy = new ResultResiliencePolicy(options, timeProvider);
        pipeline.TimeProvider = timeProvider;

        pipeline.AddTimeout(new HttpTimeoutStrategyOptions { Timeout = options.TotalTimeout });

        if (options.MaxRetries > 0)
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
            MaxRetryAttempts = options.MaxRetries,
            Delay = options.RetryDelay,
            MaxDelay = options.MaxRetryDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = options.UseJitter,
            ShouldHandle = arguments => ValueTask.FromResult(policy.ShouldRetry(arguments.Outcome)),
            // Its own reading of Retry-After, so a date is measured on the TimeProvider's clock.
            ShouldRetryAfterHeader = false,
            DelayGenerator = arguments => ValueTask.FromResult(policy.RetryAfter(arguments.Outcome.Result)),
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
