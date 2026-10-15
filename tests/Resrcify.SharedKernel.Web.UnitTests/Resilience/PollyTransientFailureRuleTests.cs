using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Resilience;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Resilience;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class PollyTransientFailureRuleTests
{
    public static TheoryData<string, Exception, bool> Failures => new()
    {
        { "Polly's timeout", new TimeoutRejectedException(), true },
        { "Polly's open circuit", new BrokenCircuitException(), true },
        { "Polly's isolated circuit", new IsolatedCircuitException(), true },
        { "Polly's rate limiter", new RateLimiterRejectedException(), true },
        { "a handler's bug", new InvalidOperationException("broken"), false },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void IsTransient_ShouldCallPollysRejectionsTransient(string description, Exception failure, bool transient)
        => new PollyTransientFailureRule().IsTransient(failure).ShouldBe(transient, description);

    [Fact]
    public void BuiltInRule_ShouldNotKnowPolly()
        => TransientFailures.IsTransient(new IsolatedCircuitException()).ShouldBeFalse();

    [Fact]
    public void AddResultResilience_ShouldAddTheRule_SoTheContainersClassifierCallsPollysRejectionsTransient()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("first").AddResultResilience();
        services.AddHttpClient("second").AddResultResilience();
        using var provider = services.BuildServiceProvider();

        var classifier = provider.GetRequiredService<ITransientFailureClassifier>();

        classifier.IsTransient(new InvalidOperationException("wrapped", new IsolatedCircuitException())).ShouldBeTrue();
        classifier.IsTransient(new RateLimiterRejectedException()).ShouldBeTrue();
        classifier.IsTransient(new InvalidOperationException("a bug")).ShouldBeFalse();
        services.Count(descriptor => descriptor.ServiceType == typeof(ITransientFailureRule)).ShouldBe(1);
    }
}
