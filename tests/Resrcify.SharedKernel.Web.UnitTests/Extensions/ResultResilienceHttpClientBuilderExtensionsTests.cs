using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Resilience;
using Resrcify.SharedKernel.Web.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

/// <summary>
/// The preset around a real <see cref="HttpClient"/> from the factory, its upstream scripted and every wait on a
/// <see cref="FakeTimeProvider"/>: the delays here are minutes and hours, so a test passes only if the pipeline waits
/// on the fake clock.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ResultResilienceHttpClientBuilderExtensionsTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task AddResultResilience_ShouldRetryAServiceUnavailable_UntilItSucceeds()
    {
        using var upstream = Upstream(call => call < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        using var provider = Provider(upstream);

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        upstream.Calls.ShouldBe(3);
        upstream.Gap(1, 2).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AddResultResilience_ShouldReturnTheLastResponse_WhenTheRetriesRunOut()
    {
        using var upstream = Upstream(_ => HttpStatusCode.BadGateway);
        using var provider = Provider(upstream);

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        upstream.Calls.ShouldBe(4);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task AddResultResilience_ShouldRetry_WhenTheStatusIsATransientErrorType(
        HttpStatusCode status)
    {
        using var upstream = Upstream(call => call == 1 ? status : HttpStatusCode.OK);
        using var provider = Provider(upstream);

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        upstream.Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task AddResultResilience_ShouldNotRetry_WhenTheStatusIsAnAnswer(
        HttpStatusCode status)
    {
        using var upstream = Upstream(_ => status);
        using var provider = Provider(upstream);

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(status);
        upstream.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task AddResultResilience_ShouldRetryANotFound_WhenItIsAlsoRetried()
    {
        using var upstream = Upstream(call => call == 1 ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        using var provider = Provider(upstream, options => options.AlsoRetry.Add(StatusCodes.NotFound));

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        upstream.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task AddResultResilience_ShouldNotRetryAServiceUnavailable_WhenItIsNeverRetried()
    {
        using var upstream = Upstream(_ => HttpStatusCode.ServiceUnavailable);
        using var provider = Provider(upstream, options => options.NeverRetry.Add(StatusCodes.ServiceUnavailable));

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        upstream.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task AddResultResilience_ShouldWaitTheRetryAfterDelay_WhenRateLimited()
    {
        using var upstream = new ScriptedHandler(_time, (call, _) => Task.FromResult(call == 1
            ? RateLimited(new RetryConditionHeaderValue(TimeSpan.FromHours(1)))
            : new HttpResponseMessage(HttpStatusCode.OK)));
        using var provider = Provider(upstream);

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        upstream.Gap(1, 2).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task AddResultResilience_ShouldWaitUntilTheRetryAfterDate_OnTheTimeProvidersClock()
    {
        var retryAt = _time.GetUtcNow().AddHours(2);
        using var upstream = new ScriptedHandler(_time, (call, _) => Task.FromResult(call == 1
            ? RateLimited(new RetryConditionHeaderValue(retryAt))
            : new HttpResponseMessage(HttpStatusCode.OK)));
        using var provider = Provider(upstream);

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        upstream.CalledAt[1].ShouldBeGreaterThanOrEqualTo(retryAt);
    }

    [Fact]
    public async Task AddResultResilience_ShouldReturnTheRateLimitAtOnce_WhenItsRetryAfterOutlastsTheTotalTimeout()
    {
        using var upstream = new ScriptedHandler(_time, (call, _) => Task.FromResult(call == 1
            ? RateLimited(new RetryConditionHeaderValue(TimeSpan.FromHours(3)))
            : new HttpResponseMessage(HttpStatusCode.OK)));
        using var provider = Provider(upstream, options => options.TotalTimeout = TimeSpan.FromHours(2));
        var started = _time.GetUtcNow();

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        upstream.Calls.ShouldBe(1);
        (_time.GetUtcNow() - started).ShouldBeLessThan(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task AddResultResilience_ShouldNotWaitARetryAfter_WhenEarlierRetriesUsedUpTheTimeItNeeds()
    {
        using var upstream = new ScriptedHandler(_time, (call, _) => Task.FromResult(call switch
        {
            1 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            2 => RateLimited(new RetryConditionHeaderValue(TimeSpan.FromMinutes(90))),
            _ => new HttpResponseMessage(HttpStatusCode.OK),
        }));
        using var provider = Provider(upstream, options =>
        {
            options.RetryDelay = TimeSpan.FromMinutes(45);
            options.TotalTimeout = TimeSpan.FromHours(2);
        });

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        upstream.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task AddResultResilience_ShouldRetry_WhenTheNetworkFails()
    {
        using var upstream = new ScriptedHandler(_time, (call, _) => call == 1
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused"))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var provider = Provider(upstream);

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        upstream.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task AddResultResilience_ShouldRetry_WhenAnAttemptTimesOut()
    {
        using var upstream = new ScriptedHandler(_time, async (call, cancellationToken) =>
        {
            if (call == 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var provider = Provider(upstream, options => options.AttemptTimeout = TimeSpan.FromMinutes(30));

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        upstream.Gap(1, 2).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task AddResultResilience_ShouldGiveUp_WhenTheTotalTimeoutPasses()
    {
        using var upstream = new ScriptedHandler(_time, async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var provider = Provider(upstream, options => options.TotalTimeout = TimeSpan.FromHours(2));

        await Should.ThrowAsync<TimeoutRejectedException>(() => SendAsync(provider));
    }

    [Fact]
    public async Task AddResultResilience_ShouldBreakTheCircuit_WhenCallsKeepFailing()
    {
        using var upstream = Upstream(_ => HttpStatusCode.ServiceUnavailable);
        using var provider = Provider(upstream, options =>
        {
            options.MaxRetries = 0;
            options.CircuitBreaker = true;
            options.CircuitBreakerMinimumThroughput = 2;
            options.CircuitBreakerFailureRatio = 0.5;
            options.CircuitBreakerSamplingDuration = TimeSpan.FromDays(1);
            options.CircuitBreakerBreakDuration = TimeSpan.FromDays(1);
        });

        (await SendAsync(provider)).Dispose();
        (await SendAsync(provider)).Dispose();

        await Should.ThrowAsync<BrokenCircuitException>(() => SendAsync(provider));
        upstream.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task AddResultResilience_ShouldNotRetry_WhenTheCallerCancels()
    {
        using var cancellation = new CancellationTokenSource();
        using var upstream = new ScriptedHandler(_time, async (_, cancellationToken) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var provider = Provider(upstream);

        await Should.ThrowAsync<OperationCanceledException>(() => SendAsync(provider, cancellation.Token));
        upstream.Calls.ShouldBe(1);
    }

    private ScriptedHandler Upstream(
        Func<int, HttpStatusCode> status)
        => new(_time, (call, _) => Task.FromResult(new HttpResponseMessage(status(call))));

    private static HttpResponseMessage RateLimited(
        RetryConditionHeaderValue retryAfter)
        => new(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = retryAfter } };

    // Long delays, so that only the fake clock gets through them; no circuit breaker unless a test turns it on.
    private ServiceProvider Provider(
        ScriptedHandler upstream,
        Action<ResultResilienceOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services
            .AddHttpClient("upstream", client =>
            {
                client.BaseAddress = new Uri("http://upstream");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => upstream)
            .AddResultResilience(options =>
            {
                options.UseJitter = false;
                options.RetryDelay = TimeSpan.FromMinutes(1);
                options.MaxRetryDelay = TimeSpan.FromHours(4);
                options.AttemptTimeout = TimeSpan.FromHours(6);
                options.TotalTimeout = TimeSpan.FromDays(1);
                options.CircuitBreaker = false;
                configure?.Invoke(options);
            });
        return services.BuildServiceProvider();
    }

    // Sends a request, moving the fake clock on until it is answered.
    private async Task<HttpResponseMessage> SendAsync(
        ServiceProvider provider,
        CancellationToken cancellationToken = default)
    {
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("upstream");
        var sending = client.GetAsync(new Uri("/players", UriKind.Relative), cancellationToken);

        for (var turn = 0; turn < 2_000 && !sending.IsCompleted; turn++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), CancellationToken.None);
            if (!sending.IsCompleted)
                _time.Advance(Step);
        }

        return await sending;
    }

    private static class StatusCodes
    {
        public const int NotFound = 404;
        public const int ServiceUnavailable = 503;
    }
}
