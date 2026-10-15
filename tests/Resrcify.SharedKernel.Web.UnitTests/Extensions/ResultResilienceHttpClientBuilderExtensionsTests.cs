using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Results.Primitives;
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
        using var provider = Provider(upstream, options => options.TotalTimeout = options.AttemptTimeout = TimeSpan.FromHours(2));
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
            options.Retries = options.Retries with { FirstDelay = TimeSpan.FromMinutes(45) };
            options.TotalTimeout = options.AttemptTimeout = TimeSpan.FromHours(2);
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
    public async Task AddResultResilience_ShouldAnswerUnreachable_WhenTheNetworkKeepsFailing()
    {
        using var upstream = new ScriptedHandler(_time, (_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused (upstream:80)")));
        using var provider = Provider(upstream, options => options.Retries = options.Retries with { MaxAttempts = 3 });

        using var response = await SendAsync(provider);

        upstream.Calls.ShouldBe(3);
        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        response.Headers.GetValues(ResultResilienceErrors.Header).ShouldBe([ResultResilienceErrors.UnreachableMarker]);
        response.RequestMessage.ShouldNotBeNull().RequestUri.ShouldBe(new Uri("http://upstream/players"));
        var expected = ResultResilienceErrors.Unreachable("upstream", "Connection refused (upstream:80)");
        await ShouldReadAsAsync(response, expected);
        expected.Code.ShouldBe("Http.Unreachable");
        expected.Type.ShouldBe(ErrorType.ExternalFailure);
        expected.IsTransient().ShouldBeTrue();
        expected.Message.ShouldBe("The service at upstream could not be reached: Connection refused (upstream:80)");
    }

    [Fact]
    public async Task AddResultResilience_ShouldThrowTheNetworkFailure_WhenAskedTo()
    {
        using var upstream = new ScriptedHandler(_time, (_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused")));
        using var provider = Provider(upstream, options =>
        {
            options.Retries = options.Retries with { MaxAttempts = 2 };
            options.ThrowTimeoutsAndOpenCircuits = true;
        });

        var thrown = await Should.ThrowAsync<HttpRequestException>(() => SendAsync(provider));

        thrown.Message.ShouldBe("Connection refused");
        upstream.Calls.ShouldBe(2);
    }

    [Fact]
    public void ResultResilienceOptions_ShouldDefaultToFourAttempts_OneSecondDoublingToThirty_WithJitter()
        => new ResultResilienceOptions().Retries
            .ShouldBe(new RetrySchedule(4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), Jitter: true));

    [Fact]
    public async Task AddResultResilience_ShouldTryOnce_WhenTheRetriesAllowOneAttempt()
    {
        using var upstream = Upstream(_ => HttpStatusCode.ServiceUnavailable);
        using var provider = Provider(upstream, options => options.Retries = options.Retries with { MaxAttempts = 1 });

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        upstream.Calls.ShouldBe(1);
    }

    [Fact]
    public void AddResultResilience_ShouldThrow_WithoutRetries()
        => Should.Throw<OptionsValidationException>(() => Register(options => options.Retries = null!));

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
    public async Task AddResultResilience_ShouldAnswerATimeout_WhenTheTotalTimeoutPasses()
    {
        using var upstream = Hanging();
        using var provider = Provider(upstream, options => options.TotalTimeout = options.AttemptTimeout = TimeSpan.FromHours(2));

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        response.Headers.GetValues(ResultResilienceErrors.Header).ShouldBe([ResultResilienceErrors.TimeoutMarker]);
        response.RequestMessage.ShouldNotBeNull().RequestUri.ShouldBe(new Uri("http://upstream/players"));
        await ShouldReadAsAsync(response, ResultResilienceErrors.Timeout);
    }

    [Fact]
    public async Task AddResultResilience_ShouldAnswerATimeout_WhenTheLastAttemptTimesOut()
    {
        using var upstream = Hanging();
        using var provider = Provider(upstream, options =>
        {
            options.Retries = options.Retries with { MaxAttempts = 3 };
            options.AttemptTimeout = TimeSpan.FromMinutes(30);
        });

        using var response = await SendAsync(provider);

        upstream.Calls.ShouldBe(3);
        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        await ShouldReadAsAsync(response, ResultResilienceErrors.Timeout);
    }

    [Fact]
    public async Task AddResultResilience_ShouldThrowTheTimeout_WhenAskedTo()
    {
        using var upstream = Hanging();
        using var provider = Provider(upstream, options =>
        {
            options.TotalTimeout = options.AttemptTimeout = TimeSpan.FromHours(2);
            options.ThrowTimeoutsAndOpenCircuits = true;
        });

        await Should.ThrowAsync<TimeoutRejectedException>(() => SendAsync(provider));
    }

    [Fact]
    public async Task AddResultResilience_ShouldAnswerAnOpenCircuit_WhenCallsKeepFailing()
    {
        using var upstream = Upstream(_ => HttpStatusCode.ServiceUnavailable);
        using var provider = Provider(upstream, BreakingQuickly);

        (await SendAsync(provider)).Dispose();
        (await SendAsync(provider)).Dispose();

        using var response = await SendAsync(provider);

        upstream.Calls.ShouldBe(2);
        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        response.Headers.GetValues(ResultResilienceErrors.Header).ShouldBe([ResultResilienceErrors.CircuitOpenMarker]);
        await ShouldReadAsAsync(response, ResultResilienceErrors.CircuitOpen);
    }

    [Fact]
    public async Task AddResultResilience_ShouldThrowTheOpenCircuit_WhenAskedTo()
    {
        using var upstream = Upstream(_ => HttpStatusCode.ServiceUnavailable);
        using var provider = Provider(upstream, options =>
        {
            BreakingQuickly(options);
            options.ThrowTimeoutsAndOpenCircuits = true;
        });

        (await SendAsync(provider)).Dispose();
        (await SendAsync(provider)).Dispose();

        await Should.ThrowAsync<BrokenCircuitException>(() => SendAsync(provider));
        upstream.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task AddResultResilience_ShouldFailEnsureSuccessStatusCode_OnAnAnsweredTimeout()
    {
        using var upstream = Hanging();
        using var provider = Provider(upstream, options => options.TotalTimeout = options.AttemptTimeout = TimeSpan.FromHours(2));

        using var response = await SendAsync(provider);

        Should.Throw<HttpRequestException>(response.EnsureSuccessStatusCode)
            .StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
    }

    [Fact]
    public async Task AddResultResilience_ShouldThrowTheCallersCancellation_WhenTimeoutsAreAnswered()
    {
        using var cancellation = new CancellationTokenSource();
        using var upstream = new ScriptedHandler(_time, async (_, cancellationToken) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var provider = Provider(upstream, options =>
        {
            options.TotalTimeout = TimeSpan.FromHours(2);
            options.AttemptTimeout = TimeSpan.FromMinutes(30);
        });

        var thrown = await Should.ThrowAsync<OperationCanceledException>(() => SendAsync(provider, cancellation.Token));

        thrown.ShouldNotBeOfType<TimeoutRejectedException>();
        upstream.Calls.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(ErrorStatuses))]
    public async Task AddResultResilience_ShouldRetryOnlyTheListedStatuses_WhenRetryOnlyIsSet(
        int status)
    {
        using var upstream = Upstream(_ => (HttpStatusCode)status);
        using var provider = Provider(upstream, options =>
        {
            options.Retries = options.Retries with { MaxAttempts = 3 };
            options.RetryOnly = ResultResilienceOptions.SafeToRetryStatuses;
        });

        using var response = await SendAsync(provider);

        ((int)response.StatusCode).ShouldBe(status);
        upstream.Calls.ShouldBe(status is 408 or 429 or 503 ? 3 : 1);
    }

    [Fact]
    public async Task AddResultResilience_ShouldStillRetryANetworkErrorAndATimedOutAttempt_WhenRetryOnlyIsEmpty()
    {
        using var upstream = new ScriptedHandler(_time, async (call, cancellationToken) =>
        {
            if (call == 1)
                throw new HttpRequestException("Connection refused");
            if (call == 2)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(call == 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });
        using var provider = Provider(upstream, options =>
        {
            options.Retries = options.Retries with { MaxAttempts = 6 };
            options.AttemptTimeout = TimeSpan.FromMinutes(30);
            options.RetryOnly = new HashSet<int>();
        });

        using var response = await SendAsync(provider);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        upstream.Calls.ShouldBe(3);
    }

    [Fact]
    public void AddResultResilience_ShouldThrow_WhenRetryOnlyIsCombinedWithAlsoRetry()
        => Should.Throw<OptionsValidationException>(() => Register(options =>
            {
                options.RetryOnly = ResultResilienceOptions.SafeToRetryStatuses;
                options.AlsoRetry.Add(StatusCodes.NotFound);
            }))
            .Message.ShouldContain("AlsoRetry");

    [Fact]
    public void AddResultResilience_ShouldThrow_WhenRetryOnlyIsCombinedWithNeverRetry()
        => Should.Throw<OptionsValidationException>(() => Register(options =>
            {
                options.RetryOnly = ResultResilienceOptions.SafeToRetryStatuses;
                options.NeverRetry.Add(StatusCodes.ServiceUnavailable);
            }))
            .Message.ShouldContain("NeverRetry");

    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(399)]
    [InlineData(600)]
    public void AddResultResilience_ShouldThrow_WhenRetryOnlyHoldsAStatusThatIsNoError(
        int status)
        => Should.Throw<OptionsValidationException>(() => Register(options =>
                options.RetryOnly = new HashSet<int> { StatusCodes.ServiceUnavailable, status }))
            .Message.ShouldContain(status.ToString(CultureInfo.InvariantCulture));

    [Fact]
    public async Task AddResultResilience_ShouldKeepTheRetryOnlyItWasGiven_WhenTheSetChangesAfterwards()
    {
        var retryOnly = new HashSet<int> { StatusCodes.ServiceUnavailable };
        using var upstream = Upstream(_ => HttpStatusCode.InternalServerError);
        using var provider = Provider(upstream, options => options.RetryOnly = retryOnly);
        _ = provider.GetRequiredService<IOptionsMonitor<ResultResilienceOptions>>().Get("upstream");   // as the host's start does
        retryOnly.Add(StatusCodes.InternalServerError);

        using var response = await SendAsync(provider);

        upstream.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData("AttemptTimeout", "00:01:00", "longer than TotalTimeout")]
    [InlineData("AttemptTimeout", "-00:00:01", "AttemptTimeout must be positive")]
    [InlineData("CircuitBreakerBreakDuration", "-00:00:01", "can't be negative")]
    [InlineData("CircuitBreakerFailureRatio", "1.5", "between 0 and 1")]
    public async Task AddResultResilience_ShouldStopTheHostsStart_WhenItsOptionsAreInvalid(
        string key,
        string value,
        string problem)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Upstream:Resilience:" + key] = value,
            ["Upstream:Resilience:TotalTimeout"] = "00:00:30",
        });
        builder.Services.AddHttpClient("upstream").AddResultResilience("Upstream:Resilience");
        using var host = builder.Build();

        var thrown = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        thrown.Message.ShouldContain("'upstream'");
        thrown.Message.ShouldContain(problem);
    }

    [Fact]
    public async Task AddResultResilience_ShouldBindItsOptions_FromTheConfigurationSection()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Upstream:Resilience:AttemptTimeout"] = "00:01:00",
            ["Upstream:Resilience:TotalTimeout"] = "00:01:35",
            ["Upstream:Resilience:CircuitBreaker"] = "false",
            ["Upstream:Resilience:Retries:MaxAttempts"] = "4",
            ["Upstream:Resilience:Retries:FirstDelay"] = "00:00:01",
            ["Upstream:Resilience:Retries:MaxDelay"] = "00:00:04",
            ["Upstream:Resilience:Retries:Jitter"] = "true",
        });
        builder.Services
            .AddHttpClient("upstream")
            .AddResultResilience(
                "Upstream:Resilience",
                resilience => resilience.RetryOnly = ResultResilienceOptions.SafeToRetryStatuses);
        using var host = builder.Build();
        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptionsMonitor<ResultResilienceOptions>>().Get("upstream");

        options.AttemptTimeout.ShouldBe(TimeSpan.FromMinutes(1));
        options.TotalTimeout.ShouldBe(TimeSpan.FromSeconds(95));
        options.CircuitBreaker.ShouldBeFalse();
        options.Retries.ShouldBe(new RetrySchedule(4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), Jitter: true));
        options.RetryOnly.ShouldNotBeNull().ShouldBe(ResultResilienceOptions.SafeToRetryStatuses, ignoreOrder: true);
        await host.StopAsync();
    }

    [Fact]
    public async Task AddResultResilience_ShouldKeepTheDefaults_ForWhatTheSectionLeavesOut()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Upstream:Resilience:TotalTimeout"] = "00:01:00",
        });
        builder.Services.AddHttpClient("upstream").AddResultResilience("Upstream:Resilience");
        using var host = builder.Build();
        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptionsMonitor<ResultResilienceOptions>>().Get("upstream");

        options.TotalTimeout.ShouldBe(TimeSpan.FromMinutes(1));
        options.AttemptTimeout.ShouldBe(TimeSpan.FromSeconds(10));
        options.Retries.ShouldBe(ResultResilienceOptions.DefaultRetries);
        await host.StopAsync();
    }

    [Fact]
    public async Task AddResultResilience_ShouldLetTheSectionOverrideTheCodesValues_OneByOne()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Upstream:Resilience:TotalTimeout"] = "00:00:02",
            ["Upstream:Resilience:Retries:FirstDelay"] = "00:00:00.001",
        });
        builder.Services
            .AddHttpClient("upstream")
            .AddResultResilience("Upstream:Resilience", resilience =>
            {
                resilience.Retries = new RetrySchedule(4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), Jitter: true);
                resilience.AttemptTimeout = TimeSpan.FromMilliseconds(200);
                resilience.TotalTimeout = TimeSpan.FromSeconds(95);
            });
        using var host = builder.Build();
        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptionsMonitor<ResultResilienceOptions>>().Get("upstream");

        options.TotalTimeout.ShouldBe(TimeSpan.FromSeconds(2));
        options.AttemptTimeout.ShouldBe(TimeSpan.FromMilliseconds(200));
        options.Retries.ShouldBe(new RetrySchedule(4, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(4), Jitter: true));
        await host.StopAsync();
    }

    [Fact]
    public void AddResultResilience_ShouldRaiseTheClientsTimeout_AboveTheTotalTimeout()
    {
        using var provider = TimedProvider(
            client => client.Timeout = TimeSpan.FromSeconds(10),
            totalTimeout: TimeSpan.FromMinutes(2));

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("upstream");

        client.Timeout.ShouldBe(TimeSpan.FromMinutes(2) + ResultResilienceHttpClientBuilderExtensions.HttpClientTimeoutMargin);
    }

    [Fact]
    public void AddResultResilience_ShouldRaiseHttpClientsDefaultTimeout_WhenTheTotalTimeoutOutlastsIt()
    {
        using var provider = TimedProvider(configureClient: null, totalTimeout: TimeSpan.FromSeconds(98));

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("upstream");

        client.Timeout.ShouldBe(TimeSpan.FromSeconds(103));
    }

    [Theory]
    [InlineData(600)]
    [InlineData(-1)]
    public void AddResultResilience_ShouldKeepTheClientsTimeout_WhenTheServiceSetItLonger(
        int seconds)
    {
        var timeout = seconds < 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(seconds);
        using var provider = TimedProvider(client => client.Timeout = timeout, totalTimeout: TimeSpan.FromMinutes(2));

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("upstream");

        client.Timeout.ShouldBe(timeout);
    }

    [Fact]
    public void AddResultResilience_ShouldKeepHttpClientsDefaultTimeout_WhenItOutlastsTheTotalTimeout()
    {
        using var provider = TimedProvider(configureClient: null, totalTimeout: TimeSpan.FromSeconds(30));

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("upstream");

        client.Timeout.ShouldBe(TimeSpan.FromSeconds(100));
    }

    // The client's timeout is set after AddResultResilience, as a ConfigureHttpClient further down a registration would.
    private static ServiceProvider TimedProvider(
        Action<HttpClient>? configureClient,
        TimeSpan totalTimeout)
    {
        var services = new ServiceCollection();
        services
            .AddHttpClient("upstream")
            .AddResultResilience(options =>
            {
                options.AttemptTimeout = TimeSpan.FromSeconds(10);
                options.TotalTimeout = totalTimeout;
            });
        if (configureClient is not null)
            services.AddHttpClient("upstream").ConfigureHttpClient(configureClient);
        return services.BuildServiceProvider();
    }

    public static TheoryData<int> ErrorStatuses()
        => [.. Enumerable.Range(400, 200)];

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

    // An upstream that never answers: every attempt runs until a timeout cancels it.
    private ScriptedHandler Hanging()
        => new(_time, async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

    private static void BreakingQuickly(
        ResultResilienceOptions options)
    {
        options.Retries = options.Retries with { MaxAttempts = 1 };
        options.CircuitBreaker = true;
        options.CircuitBreakerMinimumThroughput = 2;
        options.CircuitBreakerFailureRatio = 0.5;
        options.CircuitBreakerSamplingDuration = TimeSpan.FromDays(1);
        options.CircuitBreakerBreakDuration = TimeSpan.FromDays(1);
    }

    private static async Task ShouldReadAsAsync(
        HttpResponseMessage response,
        Error expected)
    {
        var result = await response.ToResultAsync<string>();

        result.IsFailure.ShouldBeTrue();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(expected.Code);
        error.Type.ShouldBe(expected.Type);
        error.Message.ShouldBe(expected.Message);
    }

    // Registers the client and reads its options, which validates them (as the host's start does).
    private static void Register(
        Action<ResultResilienceOptions> configure)
    {
        var services = new ServiceCollection();
        services
            .AddHttpClient("upstream")
            .AddResultResilience(configure);
        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptionsMonitor<ResultResilienceOptions>>().Get("upstream");
    }

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
                options.Retries = new RetrySchedule(4, TimeSpan.FromMinutes(1), TimeSpan.FromHours(4));
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
        public const int InternalServerError = 500;
        public const int ServiceUnavailable = 503;
    }
}
