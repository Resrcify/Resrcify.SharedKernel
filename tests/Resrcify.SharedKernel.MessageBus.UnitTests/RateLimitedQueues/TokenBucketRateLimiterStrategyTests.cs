using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.RateLimiting;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.RateLimitedQueues;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class TokenBucketRateLimiterStrategyTests
{
    [Fact]
    public void CreateLimiter_ShouldGrantTheBurstAtOnce_WhenTheBucketIsFull()
    {
        using var limiter = TokenBucketRateLimiterStrategy.Instance.CreateLimiter(
            "queue",
            new RateLimitedQueueOptions { PerSecond = 1, Burst = 3 });

        for (var i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire();
            lease.IsAcquired.ShouldBeTrue();
        }
        using var overBurst = limiter.AttemptAcquire();
        overBurst.IsAcquired.ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 3)]
    [InlineData(20, 1)]
    [InlineData(90, 10)]
    [InlineData(100, 5)]
    [InlineData(250, 10)]
    [InlineData(1000, 1)]
    [InlineData(1000, 200)]
    public void CreateOptions_ShouldRefillAtTheRate_InStepsNoShorterThanTheMinimum(
        int perSecond,
        int burst)
    {
        var options = TokenBucketRateLimiterStrategy.CreateOptions(
            new RateLimitedQueueOptions { PerSecond = perSecond, Burst = burst });

        var rate = options.TokensPerPeriod / options.ReplenishmentPeriod.TotalSeconds;
        rate.ShouldBe(perSecond, tolerance: perSecond * 1e-5);
        options.ReplenishmentPeriod.ShouldBeGreaterThanOrEqualTo(TokenBucketRateLimiterStrategy.MinimumRefillStep);
        if (options.TokensPerPeriod > 1)
        {
            // As short as allowed: one token fewer per step would make it shorter than the minimum.
            var oneTokenFewer = TimeSpan.FromSeconds((options.TokensPerPeriod - 1) / (double)perSecond);
            oneTokenFewer.ShouldBeLessThan(TokenBucketRateLimiterStrategy.MinimumRefillStep);
        }
        options.TokenLimit.ShouldBeGreaterThanOrEqualTo(Math.Max(burst, options.TokensPerPeriod));
        options.AutoReplenishment.ShouldBeTrue();
    }

    [Fact]
    public void CreateLimiter_ShouldReplenishOnItsOwn_WithTheOptionsCreateOptionsGives()
    {
        var options = new RateLimitedQueueOptions { PerSecond = 90, Burst = 10 };

        using var limiter = TokenBucketRateLimiterStrategy.Instance.CreateLimiter("queue", options);

        var bucket = limiter.ShouldBeOfType<TokenBucketRateLimiter>();
        bucket.IsAutoReplenishing.ShouldBeTrue();
        bucket.ReplenishmentPeriod.ShouldBe(TokenBucketRateLimiterStrategy.CreateOptions(options).ReplenishmentPeriod);
    }

    [Fact]
    public void CreateOptions_ShouldReachTheRate_WhenTheRateIsHigherThanTheTimerResolution()
    {
        // 90/s is one token every 11 ms, finer than the ~15.6 ms timers on Windows; the steps are 5 tokens per 55.6 ms.
        var granted = GrantsOverSimulatedTime(
            new RateLimitedQueueOptions { PerSecond = 90, Burst = 10 },
            TimeSpan.FromSeconds(2));

        // The burst of 10, then 2 s at 90/s.
        granted.ShouldBe(190);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 3)]
    [InlineData(20, 1)]
    [InlineData(100, 5)]
    [InlineData(250, 10)]
    [InlineData(1000, 1)]
    public void CreateOptions_ShouldGrantTheRateAndTheBucket_AndNoMore_OverTwoSimulatedSeconds(
        int perSecond,
        int burst)
    {
        var queueOptions = new RateLimitedQueueOptions { PerSecond = perSecond, Burst = burst };
        var options = TokenBucketRateLimiterStrategy.CreateOptions(queueOptions);

        var granted = GrantsOverSimulatedTime(queueOptions, TimeSpan.FromSeconds(2));

        // The bucket (Burst, or one step if larger) plus 2 s at the rate, short of it by less than one step when 2 s
        // isn't a whole number of steps (13 tokens per 52 ms at 250/s).
        var most = 2 * perSecond + options.TokenLimit;
        granted.ShouldBeInRange(most - options.TokensPerPeriod + 1, most);
    }

    // The strategy's bucket over simulated time: full at the start and drained, then refilled once per period, every
    // token each refill grants taken. The refill is the one .NET 10's TokenBucketRateLimiter applies on each tick of an
    // auto-replenishing bucket's timer: TokensPerPeriod, capped at TokenLimit. Its TryReplenish() can't stand in for
    // the timer: on a bucket that isn't auto-replenishing it refills by the wall-clock time since the last call.
    private static int GrantsOverSimulatedTime(
        RateLimitedQueueOptions queueOptions,
        TimeSpan duration)
    {
        var options = TokenBucketRateLimiterStrategy.CreateOptions(queueOptions);

        var granted = options.TokenLimit;
        for (var elapsed = options.ReplenishmentPeriod; elapsed <= duration; elapsed += options.ReplenishmentPeriod)
        {
            var refilled = Math.Min(options.TokenLimit, options.TokensPerPeriod);
            granted += refilled;
        }

        return granted;
    }

    [Fact]
    public void RateLimitedQueueOptions_ShouldUseTheTokenBucketStrategy_WhenNoneIsSet()
        => new RateLimitedQueueOptions().RateLimiter.ShouldBe(TokenBucketRateLimiterStrategy.Instance);
}
