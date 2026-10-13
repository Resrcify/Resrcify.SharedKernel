using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.RateLimitedQueues;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class SharedRateLimiterStrategyTests
{
    [Fact]
    public void CreateLimiter_ShouldGiveEveryQueueTheSameBudget_WhenSeveralQueuesShareIt()
    {
        var shared = SharedRateLimiterStrategy.TokenBucket(perSecond: 1, burst: 3);
        using var guild = shared.CreateLimiter("guild", new RateLimitedQueueOptions());
        using var leaderboard = shared.CreateLimiter("leaderboard", new RateLimitedQueueOptions());

        using (var lease = guild.AttemptAcquire())
            lease.IsAcquired.ShouldBeTrue();
        using (var lease = guild.AttemptAcquire())
            lease.IsAcquired.ShouldBeTrue();
        using (var lease = leaderboard.AttemptAcquire())
            lease.IsAcquired.ShouldBeTrue();

        using var overBudget = leaderboard.AttemptAcquire();
        overBudget.IsAcquired.ShouldBeFalse();
    }

    [Fact]
    public void CreateLimiter_ShouldKeepTheBudgetForTheOthers_WhenOneQueueStops()
    {
        var shared = SharedRateLimiterStrategy.TokenBucket(perSecond: 1, burst: 2);
        var stopped = shared.CreateLimiter("guild", new RateLimitedQueueOptions());
        using var running = shared.CreateLimiter("leaderboard", new RateLimitedQueueOptions());

        stopped.Dispose();

        using var lease = running.AttemptAcquire();
        lease.IsAcquired.ShouldBeTrue();
    }

    [Fact]
    public void CreateLimiter_ShouldStartAFreshBudget_WhenEveryQueueHadStopped()
    {
        var shared = SharedRateLimiterStrategy.TokenBucket(perSecond: 1, burst: 1);
        var first = shared.CreateLimiter("guild", new RateLimitedQueueOptions());
        using (var lease = first.AttemptAcquire())
            lease.IsAcquired.ShouldBeTrue();
        first.Dispose();

        using var restarted = shared.CreateLimiter("guild", new RateLimitedQueueOptions());

        using var afterRestart = restarted.AttemptAcquire();
        afterRestart.IsAcquired.ShouldBeTrue();
    }
}
