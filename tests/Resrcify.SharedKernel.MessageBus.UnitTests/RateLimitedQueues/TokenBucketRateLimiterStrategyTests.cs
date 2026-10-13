using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
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

    [Fact]
    public async Task CreateLimiter_ShouldReachItsRate_WhenTheRateIsHigherThanTheTimerResolution()
    {
        // 90/s is one token every 11 ms, finer than the ~15.6 ms timers on Windows.
        using var limiter = TokenBucketRateLimiterStrategy.Instance.CreateLimiter(
            "queue",
            new RateLimitedQueueOptions { PerSecond = 90, Burst = 10 });

        var watch = Stopwatch.StartNew();
        var granted = 0;
        while (watch.Elapsed < TimeSpan.FromSeconds(2))
        {
            using var lease = await limiter.AcquireAsync(1);
            if (lease.IsAcquired)
                granted++;
        }

        // 2 s at 90/s plus the burst of 10 is 190 at most; one token per 11 ms gave about 138 here.
        granted.ShouldBeInRange(165, 195);
    }

    [Fact]
    public void RateLimitedQueueOptions_ShouldUseTheTokenBucketStrategy_WhenNoneIsSet()
        => new RateLimitedQueueOptions().RateLimiter.ShouldBe(TokenBucketRateLimiterStrategy.Instance);
}
