using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.RateLimitedQueues;

/// <summary>What a rate-limited queue's host assumes of its options, checked when the host starts.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class RateLimitedQueueOptionsValidatorTests
{
    private const string Queue = "GetGuildRequest";

    private static readonly RateLimitedQueueOptionsValidator Validator = new(Queue);

    [Fact]
    public void Validate_ShouldPass_WhenTheOptionsAreTheDefaults()
        => Validator.Validate(Queue, new RateLimitedQueueOptions()).Succeeded.ShouldBeTrue();

    [Fact]
    public void Validate_ShouldSkip_WhenTheOptionsAreAnotherQueues()
        => Validator.Validate("PlayerProfileRequest", new RateLimitedQueueOptions { PerSecond = 0 }).Skipped.ShouldBeTrue();

    [Fact]
    public void Validate_ShouldPass_WhenBurstIsZeroAndPrefetchIsOne()
        => Validator.Validate(Queue, new RateLimitedQueueOptions { Burst = 0, Prefetch = 1 }).Succeeded.ShouldBeTrue();

    [Fact]
    public void Validate_ShouldNameEveryWrongKey_WhenSeveralAreWrong()
    {
        var result = Validator.Validate(Queue, new RateLimitedQueueOptions
        {
            PerSecond = 0,
            Burst = -2,
            Prefetch = 0,
            HealthCheckTag = " ",
            HealthCheckInterval = TimeSpan.Zero,
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldBe(
        [
            "The rate-limited queue GetGuildRequest's PerSecond must be at least 1; it is 0.",
            "The rate-limited queue GetGuildRequest's Burst can't be negative; it is -2.",
            "The rate-limited queue GetGuildRequest's Prefetch must be at least 1 (or unset: one second of work); it is 0.",
            "The rate-limited queue GetGuildRequest's HealthCheckTag must name a tag (or be unset: no gating); it is ' '.",
            "The rate-limited queue GetGuildRequest's HealthCheckInterval must be positive and at most 49.7 days; it is 00:00:00.",
        ]);
    }

    [Fact]
    public void Validate_ShouldFail_WhenTheHealthCheckIntervalIsNegative()
        => Validator.Validate(Queue, new RateLimitedQueueOptions { HealthCheckInterval = TimeSpan.FromSeconds(-1) })
            .FailureMessage.ShouldNotBeNull().ShouldContain("HealthCheckInterval must be positive");

    [Fact]
    public void Validate_ShouldFail_WhenTheRateLimiterOrTheFailureRuleIsCleared()
    {
        var result = Validator.Validate(Queue, new RateLimitedQueueOptions { RateLimiter = null!, AnswerFailure = null! });

        result.Failures.ShouldBe(
        [
            "The rate-limited queue GetGuildRequest's RateLimiter is required (TokenBucketRateLimiterStrategy.Instance by default).",
            "The rate-limited queue GetGuildRequest's AnswerFailure is required (AnswerFailureWhenItsTheRequestsFault by default).",
        ]);
    }

    [Fact]
    public void Validate_ShouldFail_WhenARetryWaitIsLongerThanTaskDelayTakes()
    {
        var result = Validator.Validate(Queue, new RateLimitedQueueOptions
        {
            Retries = new RetrySchedule(3, TimeSpan.FromDays(60), TimeSpan.MaxValue),
        });

        var failures = result.Failures.ShouldNotBeNull().ToList();
        failures.Count.ShouldBe(2);
        failures.ShouldAllBe(failure => failure.StartsWith("The rate-limited queue GetGuildRequest's Retries:", StringComparison.Ordinal));
        var message = result.FailureMessage.ShouldNotBeNull();
        message.ShouldContain("Retries:FirstDelay must be between 0 and 49.7 days");
        message.ShouldContain("Retries:MaxDelay must be between 0 and 49.7 days");
    }

    [Fact]
    public void Validate_ShouldPass_WhenTheRetriesAreSet()
        => Validator.Validate(Queue, new RateLimitedQueueOptions
        {
            Retries = new RetrySchedule(6, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(2)),
        }).ShouldBe(ValidateOptionsResult.Success);
}
