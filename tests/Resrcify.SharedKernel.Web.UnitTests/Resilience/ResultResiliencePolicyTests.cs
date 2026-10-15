using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Resilience;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Resilience;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ResultResiliencePolicyTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(429, true)]
    [InlineData(408, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(409, false)]
    [InlineData(405, false)]
    [InlineData(410, false)]
    [InlineData(412, false)]
    [InlineData(413, false)]
    [InlineData(415, false)]
    [InlineData(422, false)]
    [InlineData(200, false)]
    [InlineData(204, false)]
    [InlineData(304, false)]
    public void ShouldRetry_ShouldFollowTheStatusErrorType(
        int status,
        bool retried)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)status);

        Policy().ShouldRetry(Outcome.FromResult(response)).ShouldBe(retried);
    }

    [Fact]
    public void ShouldRetry_ShouldRetryAnAlsoRetriedStatus()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound);

        Policy(options => options.AlsoRetry.Add(404)).ShouldRetry(Outcome.FromResult(response)).ShouldBeTrue();
    }

    [Fact]
    public void ShouldRetry_ShouldNotRetryANeverRetriedStatus_EvenWhenAlsoRetried()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        Policy(options =>
            {
                options.NeverRetry.Add(503);
                options.AlsoRetry.Add(503);
            })
            .ShouldRetry(Outcome.FromResult(response))
            .ShouldBeFalse();
    }

    [Fact]
    public void ShouldRetry_ShouldRetryANetworkErrorAndATimedOutAttempt()
    {
        Policy().ShouldRetry(Outcome.FromException<HttpResponseMessage>(new HttpRequestException("refused"))).ShouldBeTrue();
        Policy().ShouldRetry(Outcome.FromException<HttpResponseMessage>(new TimeoutRejectedException())).ShouldBeTrue();
    }

    [Fact]
    public void ShouldRetry_ShouldRetryWhatSharedKernelsRuleCallsTransient_ButNotARejection()
    {
        Policy().ShouldRetry(Outcome.FromException<HttpResponseMessage>(new TimeoutException("too slow"))).ShouldBeTrue();
        Policy()
            .ShouldRetry(Outcome.FromException<HttpResponseMessage>(new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable)))
            .ShouldBeTrue();
        Policy()
            .ShouldRetry(Outcome.FromException<HttpResponseMessage>(new HttpRequestException("no such thing", null, HttpStatusCode.NotFound)))
            .ShouldBeFalse();
        Policy().ShouldRetry(Outcome.FromException<HttpResponseMessage>(new InvalidOperationException("a bug"))).ShouldBeFalse();
        Policy().ShouldRetry(Outcome.FromException<HttpResponseMessage>(new BrokenCircuitException())).ShouldBeFalse();
        Policy().ShouldRetry(Outcome.FromException<HttpResponseMessage>(new IsolatedCircuitException())).ShouldBeFalse();
        Policy().ShouldRetry(Outcome.FromException<HttpResponseMessage>(new RateLimiterRejectedException())).ShouldBeFalse();
    }

    [Fact]
    public void ShouldRetry_ShouldNotRetryACancellation()
        => Policy().ShouldRetry(Outcome.FromException<HttpResponseMessage>(new OperationCanceledException())).ShouldBeFalse();

    [Fact]
    public void CountsAgainstTheCircuit_ShouldNotCountAnAlsoRetriedStatus()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound);

        Policy(options => options.AlsoRetry.Add(404)).CountsAgainstTheCircuit(Outcome.FromResult(response)).ShouldBeFalse();
    }

    [Fact]
    public void CountsAgainstTheCircuit_ShouldCountATransientFailure()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        Policy().CountsAgainstTheCircuit(Outcome.FromResult(response)).ShouldBeTrue();
    }

    [Fact]
    public void ShouldRetry_ShouldRetryOnlyTheListedStatuses_WhenRetryOnlyIsSet()
    {
        var policy = Policy(options => options.RetryOnly = ResultResilienceOptions.SafeToRetryStatuses);

        for (var status = 400; status < 600; status++)
        {
            using var response = new HttpResponseMessage((HttpStatusCode)status);
            policy.ShouldRetry(Outcome.FromResult(response)).ShouldBe(status is 408 or 429 or 503, $"status {status}");
        }
    }

    [Fact]
    public void ShouldRetry_ShouldRetryANetworkErrorAndATimedOutAttempt_WhenRetryOnlyIsEmpty()
    {
        var policy = Policy(options => options.RetryOnly = new HashSet<int>());

        policy.ShouldRetry(Outcome.FromException<HttpResponseMessage>(new HttpRequestException("refused"))).ShouldBeTrue();
        policy.ShouldRetry(Outcome.FromException<HttpResponseMessage>(new TimeoutRejectedException())).ShouldBeTrue();
        policy.ShouldRetry(Outcome.FromException<HttpResponseMessage>(new OperationCanceledException())).ShouldBeFalse();
    }

    [Fact]
    public void ShouldRetry_ShouldRetryAListedAnswer_WhenRetryOnlyListsIt()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound);

        Policy(options => options.RetryOnly = new HashSet<int> { 404 })
            .ShouldRetry(Outcome.FromResult(response))
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData(503, true)]
    [InlineData(500, false)]
    [InlineData(504, false)]
    [InlineData(408, true)]
    [InlineData(404, false)]
    public void CountsAgainstTheCircuit_ShouldCountOnlyListedTransientStatuses_WhenRetryOnlyIsSet(
        int status,
        bool counted)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)status);

        Policy(options => options.RetryOnly = new HashSet<int> { 503, 408, 404 })
            .CountsAgainstTheCircuit(Outcome.FromResult(response))
            .ShouldBe(counted);
    }

    [Fact]
    public void AnswersInPlaceOf_ShouldBeATimeoutAnOpenCircuitOrNoAnswer_NeverACancellation()
    {
        Policy().AnswersInPlaceOf(new TimeoutRejectedException()).ShouldBeTrue();
        Policy().AnswersInPlaceOf(new BrokenCircuitException()).ShouldBeTrue();
        Policy().AnswersInPlaceOf(new IsolatedCircuitException()).ShouldBeTrue();
        Policy().AnswersInPlaceOf(new HttpRequestException("refused")).ShouldBeTrue();
        Policy().AnswersInPlaceOf(new HttpRequestException("answered", null, HttpStatusCode.ServiceUnavailable)).ShouldBeFalse();
        Policy().AnswersInPlaceOf(new OperationCanceledException()).ShouldBeFalse();
        Policy().AnswersInPlaceOf(null).ShouldBeFalse();
    }

    [Fact]
    public void AnswersInPlaceOf_ShouldBeNothing_WhenTheyAreThrown()
    {
        var policy = Policy(options => options.ThrowTimeoutsAndOpenCircuits = true);

        policy.AnswersInPlaceOf(new TimeoutRejectedException()).ShouldBeFalse();
        policy.AnswersInPlaceOf(new BrokenCircuitException()).ShouldBeFalse();
        policy.AnswersInPlaceOf(new HttpRequestException("refused")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(10, 29, true)]
    [InlineData(10, 30, false)]
    [InlineData(25, 15, false)]
    [InlineData(25, 4, true)]
    public void ShouldRetry_ShouldWaitARetryAfter_OnlyWhenItEndsBeforeTheTotalTimeout(
        int elapsedSeconds,
        int retryAfterSeconds,
        bool retried)
    {
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            context.Properties.Set(ResultResiliencePolicy.CallStarted, _time.GetTimestamp());
            _time.Advance(TimeSpan.FromSeconds(elapsedSeconds));
            using var response = RateLimited(new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds)));

            Policy(options => options.TotalTimeout = TimeSpan.FromSeconds(40))
                .ShouldRetry(Outcome.FromResult(response), context)
                .ShouldBe(retried);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public void ShouldRetry_ShouldWaitAnyRetryAfter_WhenTheCallsStartIsUnknown()
    {
        using var response = RateLimited(new RetryConditionHeaderValue(TimeSpan.FromDays(3)));

        Policy().ShouldRetry(Outcome.FromResult(response)).ShouldBeTrue();
    }

    [Fact]
    public void RetryAfter_ShouldBeTheDelay_WhenTheHeaderIsADelay()
    {
        using var response = RateLimited(new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)));

        Policy().RetryAfter(response).ShouldBe(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void RetryAfter_ShouldBeTheTimeUntilTheDate_OnTheTimeProvidersClock()
    {
        using var response = RateLimited(new RetryConditionHeaderValue(_time.GetUtcNow().AddMinutes(3)));

        Policy().RetryAfter(response).ShouldBe(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void RetryAfter_ShouldBeZero_WhenTheDateHasPassed()
    {
        using var response = RateLimited(new RetryConditionHeaderValue(_time.GetUtcNow().AddMinutes(-3)));

        Policy().RetryAfter(response).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void RetryAfter_ShouldBeNull_WithoutTheHeader()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        Policy().RetryAfter(response).ShouldBeNull();
        Policy().RetryAfter(null).ShouldBeNull();
    }

    private ResultResiliencePolicy Policy(
        Action<ResultResilienceOptions>? configure = null)
    {
        var options = new ResultResilienceOptions();
        configure?.Invoke(options);
        return new ResultResiliencePolicy(options, _time);
    }

    private static HttpResponseMessage RateLimited(
        RetryConditionHeaderValue retryAfter)
        => new(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = retryAfter } };

    [Fact]
    public void ResultResilienceErrors_ShouldBeCodedFromResultsCatalogue()
    {
        ResultResilienceErrors.Timeout.Code.ShouldBe(ErrorCodes.Http.ResilienceTimeout);
        ResultResilienceErrors.CircuitOpen.Code.ShouldBe(ErrorCodes.Http.CircuitOpen);
        ResultResilienceErrors.Unreachable("upstream", "refused").Code.ShouldBe(ErrorCodes.Http.Unreachable);
    }
}
