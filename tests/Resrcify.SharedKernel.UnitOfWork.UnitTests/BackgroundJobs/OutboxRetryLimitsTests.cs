using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Resilience;
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
    public void Default_ShouldGiveThreeAttempts_AndTwelveToATransientFailure_FiveSecondsDoublingToFiveMinutes()
    {
        var limits = OutboxRetryLimits.Default;

        limits.Retries.ShouldBe(new RetrySchedule(3, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)));
        limits.TransientRetries.ShouldBe(new RetrySchedule(12, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)));
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
    public void Create_ShouldGiveATransientFailureTheHigherOfTwelveAndTheRetriesAttempts_WhenItIsUnset(
        int maxAttempts,
        int expected)
    {
        var retries = Schedule(maxAttempts) with { Jitter = true };

        var limits = OutboxRetryLimits.Create(retries, transientRetries: null);

        limits.Retries.ShouldBeSameAs(retries);
        limits.TransientRetries.ShouldBe(retries with { MaxAttempts = expected });
        limits.ReadLimit.ShouldBe(expected);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(3, 5)]
    [InlineData(20, 30)]
    public void Create_ShouldKeepAnExplicitTransientSchedule_WhenItHasAtLeastTheRetriesAttempts(
        int maxAttempts,
        int transientMaxAttempts)
    {
        var transient = new RetrySchedule(transientMaxAttempts, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));

        var limits = OutboxRetryLimits.Create(Schedule(maxAttempts), transient);

        limits.MaxRetryCount.ShouldBe(maxAttempts);
        limits.TransientRetries.ShouldBeSameAs(transient);
        limits.ReadLimit.ShouldBe(transientMaxAttempts);
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(13, 12)]
    public void Create_ShouldThrow_WhenAnExplicitTransientScheduleHasFewerAttempts(
        int maxAttempts,
        int transientMaxAttempts)
        => Should.Throw<ArgumentOutOfRangeException>(() => OutboxRetryLimits.Create(
                Schedule(maxAttempts),
                Schedule(transientMaxAttempts),
                "regular",
                "transient"))
            .ParamName.ShouldBe("transient");

    [Fact]
    public void Create_ShouldThrow_WithoutRetries()
        => Should.Throw<ArgumentNullException>(() => OutboxRetryLimits.Create(null!, null, "regular", "transient"))
            .ParamName.ShouldBe("regular");

    [Fact]
    public void Create_ShouldNameTheOptions_WhenNoNamesAreGiven()
        => Should.Throw<ArgumentOutOfRangeException>(() => OutboxRetryLimits.Create(Schedule(5), Schedule(4)))
            .ParamName.ShouldBe(nameof(OutboxRetryLimits.TransientRetries));

    [Fact]
    public void For_ShouldGiveATransientFailureTheTransientSchedule()
    {
        var limits = OutboxRetryLimits.Default;

        var limit = limits.For(new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable), classifier: null);

        limit.ShouldBe(new OutboxFailureLimit(limits.TransientRetries, Transient: true));
        limit.MaxTries.ShouldBe(12);
    }

    [Fact]
    public void For_ShouldGiveANonTransientFailureTheRegularSchedule()
    {
        var limits = OutboxRetryLimits.Default;

        var limit = limits.For(new InvalidOperationException("a handler's bug"), classifier: null);

        limit.ShouldBe(new OutboxFailureLimit(limits.Retries, Transient: false));
        limit.MaxTries.ShouldBe(3);
    }

    [Fact]
    public void For_ShouldAskTheContainersClassifier_WhenThereIsOne()
    {
        // A service's own rule: its outage exception is transient.
        using var provider = new ServiceCollection()
            .AddTransientFailureRule(exception => exception is IOException)
            .BuildServiceProvider();
        var classifier = provider.GetRequiredService<ITransientFailureClassifier>();
        var limits = OutboxRetryLimits.Default;

        limits.For(new IOException("the connection broke"), classifier).Transient.ShouldBeTrue();
        limits.For(new IOException("the connection broke"), classifier: null).Transient.ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(6, true)]
    public void GivesUpAt_ShouldGiveUp_OnceTheTryReachesTheLimit(int attempt, bool givesUp)
        => new OutboxFailureLimit(Schedule(3), Transient: false).GivesUpAt(attempt).ShouldBe(givesUp);

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(6, 160)]
    [InlineData(7, 300)]
    [InlineData(40, 300)]
    public void DelayAfter_ShouldWaitTheSchedulesDelay(int attempt, int seconds)
        => new OutboxFailureLimit(OutboxRetryLimits.DefaultRetries, Transient: false)
            .DelayAfter(attempt)
            .ShouldBe(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void DelayAfter_ShouldBeNone_WhenTheScheduleDoesntWait()
        => new OutboxFailureLimit(new RetrySchedule(3, TimeSpan.Zero, TimeSpan.Zero), Transient: false)
            .DelayAfter(1)
            .ShouldBeNull();

    [Fact]
    public void For_ShouldWaitTheTransientSchedulesDelays_ForATransientFailure()
    {
        var limits = OutboxRetryLimits.Create(
            Schedule(3),
            new RetrySchedule(12, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8)));

        var transient = limits.For(new TimeoutException("too slow"), classifier: null);
        var other = limits.For(new InvalidOperationException("a bug"), classifier: null);

        transient.DelayAfter(5).ShouldBe(TimeSpan.FromSeconds(8));
        other.DelayAfter(2).ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Options_ShouldDefaultToThreeAttempts_FiveSecondsToFiveMinutes_AndAnUnsetTransientSchedule()
    {
        var outbox = new Resrcify.SharedKernel.UnitOfWork.Extensions.OutboxOptions();
        var lanes = new Resrcify.SharedKernel.UnitOfWork.Extensions.OutboxLaneOptions();

        outbox.Retries.ShouldBe(new RetrySchedule(3, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)));
        outbox.TransientRetries.ShouldBeNull();
        lanes.Retries.ShouldBe(outbox.Retries);
        lanes.TransientRetries.ShouldBeNull();
    }

    private static RetrySchedule Schedule(int maxAttempts)
        => new(maxAttempts, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
}
