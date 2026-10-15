using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Abstractions.UnitTests.Resilience;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class RetryScheduleTests
{
    private static readonly RetrySchedule FiveSecondsToFiveMinutes = new(12, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(6, 160)]
    [InlineData(7, 300)]
    [InlineData(39, 300)]
    [InlineData(int.MaxValue, 300)]
    public void DelayAfter_ShouldDoubleFromTheFirstDelay_UpToTheLongest(int failedAttempt, int seconds)
        => FiveSecondsToFiveMinutes.DelayAfter(failedAttempt).ShouldBe(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void DelayAfter_ShouldNotOverflow_WhateverTheDelaysAndTheAttempt()
    {
        var huge = new RetrySchedule(int.MaxValue, TimeSpan.FromDays(4_000_000), TimeSpan.MaxValue);
        var tiny = new RetrySchedule(int.MaxValue, TimeSpan.FromTicks(1), TimeSpan.MaxValue);

        huge.DelayAfter(int.MaxValue).ShouldBe(TimeSpan.MaxValue);
        huge.DelayAfter(2).ShouldBe(TimeSpan.FromDays(8_000_000));
        tiny.DelayAfter(int.MaxValue).ShouldBe(TimeSpan.MaxValue);
        tiny.DelayAfter(64).ShouldBe(TimeSpan.MaxValue);
        tiny.DelayAfter(63).ShouldBe(TimeSpan.FromTicks(1L << 62));
    }

    [Fact]
    public void DelayAfter_ShouldCapEveryDelay_WhenTheLongestIsShorterThanTheFirst()
    {
        var schedule = new RetrySchedule(5, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30));

        schedule.DelayAfter(1).ShouldBe(TimeSpan.FromSeconds(30));
        schedule.DelayAfter(4).ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void DelayAfter_ShouldBeZero_WhenTheFirstDelayIs()
        => new RetrySchedule(5, TimeSpan.Zero, TimeSpan.FromMinutes(1)).DelayAfter(4).ShouldBe(TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(1_000)]
    public void DelayAfter_ShouldSpreadAJitteredDelay_BetweenHalfOfItAndAllOfIt(int failedAttempt)
    {
        var jittered = FiveSecondsToFiveMinutes with { Jitter = true };
        var delay = FiveSecondsToFiveMinutes.DelayAfter(failedAttempt);

        var delays = Enumerable.Range(0, 500).Select(_ => jittered.DelayAfter(failedAttempt)).ToList();

        delays.ShouldAllBe(jitteredDelay => jitteredDelay >= delay / 2 && jitteredDelay <= delay);
        delays.Distinct().Count().ShouldBeGreaterThan(1);
    }

    [Fact]
    public void DelayAfter_ShouldNotOverflow_WhenAJitteredDelayIsTheLongestTimeSpan()
    {
        var schedule = new RetrySchedule(int.MaxValue, TimeSpan.MaxValue, TimeSpan.MaxValue, Jitter: true);

        for (var i = 0; i < 100; i++)
            schedule.DelayAfter(int.MaxValue).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromTicks(TimeSpan.MaxValue.Ticks / 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DelayAfter_ShouldThrow_BeforeTheFirstAttempt(int failedAttempt)
        => Should.Throw<ArgumentOutOfRangeException>(() => FiveSecondsToFiveMinutes.DelayAfter(failedAttempt));

    [Theory]
    [InlineData(1, false)]
    [InlineData(11, true)]
    [InlineData(12, false)]
    public void HasAttemptAfter_ShouldCountAttempts_TheFirstIncluded(int failedAttempt, bool expected)
    {
        var schedule = FiveSecondsToFiveMinutes with { MaxAttempts = failedAttempt == 1 ? 1 : 12 };

        schedule.HasAttemptAfter(failedAttempt).ShouldBe(expected);
    }

    [Fact]
    public void New_ShouldRefuseFewerThanOneAttempt_OrANegativeDelay()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new RetrySchedule(0, TimeSpan.Zero, TimeSpan.Zero))
            .ParamName.ShouldBe(nameof(RetrySchedule.MaxAttempts));
        Should.Throw<ArgumentOutOfRangeException>(() => new RetrySchedule(1, TimeSpan.FromSeconds(-1), TimeSpan.Zero))
            .ParamName.ShouldBe(nameof(RetrySchedule.FirstDelay));
        Should.Throw<ArgumentOutOfRangeException>(() => new RetrySchedule(1, TimeSpan.Zero, TimeSpan.FromSeconds(-1)))
            .ParamName.ShouldBe(nameof(RetrySchedule.MaxDelay));
    }

    [Fact]
    public void With_ShouldValidateToo()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => FiveSecondsToFiveMinutes with { MaxAttempts = 0 });
        Should.Throw<ArgumentOutOfRangeException>(() => FiveSecondsToFiveMinutes with { FirstDelay = TimeSpan.FromTicks(-1) });
    }

    [Fact]
    public void With_ShouldChangeOneValue_AndKeepTheOthers()
    {
        var changed = FiveSecondsToFiveMinutes with { MaxAttempts = 3 };

        changed.ShouldBe(new RetrySchedule(3, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)));
        changed.ShouldNotBe(FiveSecondsToFiveMinutes);
    }
}
