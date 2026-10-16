using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
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
    /// <c>Microsoft.Extensions.Http.Resilience</c> comes with this package. After the retries the caller gets the
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
    /// <para>
    /// The options are the client's named <see cref="ResultResilienceOptions"/> (named after the client), validated when
    /// the host starts (<c>ValidateOnStart</c>), so a host with contradictory ones doesn't start
    /// (<see cref="OptionsValidationException"/>): an attempt timeout longer than the total timeout, a negative time,
    /// no <see cref="ResultResilienceOptions.Retries"/>, or <see cref="ResultResilienceOptions.RetryOnly"/> with
    /// <see cref="ResultResilienceOptions.AlsoRetry"/> or <see cref="ResultResilienceOptions.NeverRetry"/>, or a status
    /// outside 400-599 in it. A container used without a host gets that exception when it first builds the pipeline.
    /// </para>
    /// <para>
    /// The client's own <see cref="HttpClient.Timeout"/> (100 s by default) is raised to the total timeout plus
    /// <see cref="HttpClientTimeoutMargin"/> when it is shorter, after every other configuration of the client, so the
    /// pipeline's total timeout always ends a call first (as the <see cref="ResultResilienceErrors.Timeout"/> result,
    /// not HttpClient's <see cref="TaskCanceledException"/>). One the service set longer, or infinite, is kept.
    /// </para>
    /// </remarks>
    public static IHttpResiliencePipelineBuilder AddResultResilience(
        this IHttpClientBuilder builder,
        Action<ResultResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Services.AddOptions<ResultResilienceOptions>(builder.Name);
        if (configure is not null)
            options.Configure(configure);
        return builder.AddResultResilience(options);
    }

    /// <summary>
    /// <see cref="AddResultResilience(IHttpClientBuilder, Action{ResultResilienceOptions}?)"/>, with the options set by
    /// <paramref name="configure"/> (the service's own defaults), then bound from the configuration section at
    /// <paramref name="configSectionPath"/> (e.g. <c>"SwgohApiService:Resilience"</c>, with <c>AttemptTimeout</c>,
    /// <c>TotalTimeout</c>, <c>Retries:MaxAttempts</c> / <c>FirstDelay</c> / <c>MaxDelay</c> / <c>Jitter</c>,
    /// <c>CircuitBreaker</c> ...), which wins for every value it holds; a value it leaves out keeps the code's (or the
    /// default). Read when the options are first read and validated when the host starts. The configuration is the
    /// container's <c>IConfiguration</c>: the service passes no <c>IConfiguration</c> itself.
    /// </summary>
    /// <remarks>
    /// <c>Retries</c>, a <see cref="RetrySchedule"/>, is bound value by value too: a section with only
    /// <c>Retries:FirstDelay</c> keeps the schedule's attempts.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddHttpClient&lt;ISwgohApiClient, SwgohApiClient&gt;()
    ///     .AddResultResilience("SwgohApiService:Resilience", resilience =&gt;
    ///         resilience.RetryOnly = ResultResilienceOptions.SafeToRetryStatuses);
    /// </code>
    /// </example>
    public static IHttpResiliencePipelineBuilder AddResultResilience(
        this IHttpClientBuilder builder,
        string configSectionPath,
        Action<ResultResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSectionPath);

        // The code's values first, so the section (e.g. a test's shorter timeouts) overrides them.
        var options = builder.Services.AddOptions<ResultResilienceOptions>(builder.Name);
        if (configure is not null)
            options.Configure(configure);
        options.BindConfiguration(configSectionPath);
        return builder.AddResultResilience(options);
    }

    /// <summary>
    /// How much longer than the pipeline's total timeout the client's own <see cref="HttpClient.Timeout"/> is made
    /// when it is shorter (5 s), so the pipeline ends a call first.
    /// </summary>
    public static readonly TimeSpan HttpClientTimeoutMargin = TimeSpan.FromSeconds(5);

    private static IHttpResiliencePipelineBuilder AddResultResilience(
        this IHttpClientBuilder builder,
        OptionsBuilder<ResultResilienceOptions> options)
    {
        var name = builder.Name;
        options
            .PostConfigure(resilience => resilience.FreezeRetryOnly())
            .ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ResultResilienceOptions>, ResultResilienceOptionsValidator>());
        builder.Services.AddTransientFailureRule<PollyTransientFailureRule>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        // After every other configuration of the client (a PostConfigure runs after every Configure, which is how
        // AddHttpClient and ConfigureHttpClient add theirs), so the timeout it ends with is never shorter than the call's.
        builder.Services
            .AddOptions<HttpClientFactoryOptions>(name)
            .PostConfigure<IOptionsMonitor<ResultResilienceOptions>>((factory, resilience) =>
                factory.HttpClientActions.Add(client => OutlastTheTotalTimeout(client, resilience.Get(name))));

        return builder.AddResilienceHandler(
            PipelineName,
            (pipeline, context) => Configure(
                pipeline,
                context.ServiceProvider.GetRequiredService<IOptionsMonitor<ResultResilienceOptions>>().Get(name),
                context.ServiceProvider.GetRequiredService<TimeProvider>()));
    }

    // Raises a client's timeout that would end a call before the pipeline's total timeout does; never lowers one.
    private static void OutlastTheTotalTimeout(
        HttpClient client,
        ResultResilienceOptions options)
    {
        if (client.Timeout == Timeout.InfiniteTimeSpan)
            return;

        var needed = options.TotalTimeout + HttpClientTimeoutMargin;
        if (client.Timeout < needed)
            client.Timeout = needed;
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
