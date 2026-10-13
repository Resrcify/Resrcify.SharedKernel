using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Time.Testing;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class JobHeartbeatRegistryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LastBeat_ShouldBeNull_UntilTheJobBeats_AndThenTheTimeOfItsLastBeat()
    {
        var clock = new FakeTimeProvider(Start);
        var registry = new JobHeartbeatRegistry(clock);
        var job = new JobKey("ranks");

        registry.LastBeat(job).ShouldBeNull();
        registry.StartedAt.ShouldBe(Start);

        clock.Advance(TimeSpan.FromMinutes(1));
        registry.Beat(job);
        clock.Advance(TimeSpan.FromMinutes(1));
        registry.Beat(job);

        registry.LastBeat(job).ShouldBe(Start.AddMinutes(2));
        registry.LastBeat(new JobKey("payouts")).ShouldBeNull();
    }
}
