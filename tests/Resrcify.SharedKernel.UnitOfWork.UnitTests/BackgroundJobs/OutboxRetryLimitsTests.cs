using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxRetryLimitsTests
{
    [Fact]
    public void Default_ShouldGiveThreeTries_AndTwelveToATransientFailure()
    {
        var limits = OutboxRetryLimits.Default;

        limits.MaxRetryCount.ShouldBe(3);
        limits.MaxTransientRetryCount.ShouldBe(12);
        limits.ReadLimit.ShouldBe(12);
    }

    [Theory]
    [InlineData(1, 12)]
    [InlineData(5, 12)]
    [InlineData(12, 12)]
    [InlineData(13, 13)]
    [InlineData(20, 20)]
    public void Create_ShouldGiveATransientFailureTheHigherOfTwelveAndMaxRetryCount_WhenItsCountIsUnset(
        int maxRetryCount,
        int expected)
    {
        var limits = OutboxRetryLimits.Create(maxRetryCount, maxTransientRetryCount: null);

        limits.MaxRetryCount.ShouldBe(maxRetryCount);
        limits.MaxTransientRetryCount.ShouldBe(expected);
        limits.ReadLimit.ShouldBe(expected);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(3, 5)]
    [InlineData(20, 30)]
    public void Create_ShouldKeepAnExplicitTransientCount_WhenItIsAtLeastMaxRetryCount(
        int maxRetryCount,
        int maxTransientRetryCount)
    {
        var limits = OutboxRetryLimits.Create(maxRetryCount, maxTransientRetryCount);

        limits.MaxRetryCount.ShouldBe(maxRetryCount);
        limits.MaxTransientRetryCount.ShouldBe(maxTransientRetryCount);
        limits.ReadLimit.ShouldBe(maxTransientRetryCount);
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(13, 12)]
    public void Create_ShouldThrow_WhenAnExplicitTransientCountIsBelowMaxRetryCount(
        int maxRetryCount,
        int maxTransientRetryCount)
        => Should.Throw<ArgumentOutOfRangeException>(() => OutboxRetryLimits.Create(
                maxRetryCount,
                maxTransientRetryCount,
                "regular",
                "transient"))
            .ParamName.ShouldBe("transient");

    [Theory]
    [InlineData(0, null)]
    [InlineData(0, 12)]
    [InlineData(-1, null)]
    public void Create_ShouldThrow_WhenMaxRetryCountIsBelowOne(int maxRetryCount, int? maxTransientRetryCount)
        => Should.Throw<ArgumentOutOfRangeException>(() => OutboxRetryLimits.Create(
                maxRetryCount,
                maxTransientRetryCount,
                "regular",
                "transient"))
            .ParamName.ShouldBe("regular");

    [Fact]
    public void Create_ShouldNameTheOptions_WhenNoNamesAreGiven()
        => Should.Throw<ArgumentOutOfRangeException>(() => OutboxRetryLimits.Create(5, 4))
            .ParamName.ShouldBe(nameof(OutboxRetryLimits.MaxTransientRetryCount));

    [Fact]
    public void For_ShouldGiveATransientFailureTheTransientTries()
    {
        var limits = OutboxRetryLimits.Create(maxRetryCount: 3, maxTransientRetryCount: 12);

        var limit = limits.For(new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable));

        limit.ShouldBe(new OutboxFailureLimit(MaxTries: 12, Transient: true));
    }

    [Fact]
    public void For_ShouldGiveANonTransientFailureTheRegularTries()
    {
        var limits = OutboxRetryLimits.Create(maxRetryCount: 3, maxTransientRetryCount: 12);

        var limit = limits.For(new InvalidOperationException("a handler's bug"));

        limit.ShouldBe(new OutboxFailureLimit(MaxTries: 3, Transient: false));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(6, true)]
    public void GivesUpAt_ShouldGiveUp_OnceTheTryReachesTheLimit(int attempt, bool givesUp)
        => new OutboxFailureLimit(MaxTries: 3, Transient: false).GivesUpAt(attempt).ShouldBe(givesUp);
}
