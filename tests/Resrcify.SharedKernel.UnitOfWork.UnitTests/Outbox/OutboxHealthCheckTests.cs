using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;
using static Resrcify.SharedKernel.UnitOfWork.UnitTests.Models.OutboxBacklogTestHost;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Outbox;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxHealthCheckTests
{
    private static readonly TimeSpan MaxWaitingAge = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task CheckHealthAsync_ShouldBeHealthy_BeforeTheFirstMeasurement()
    {
        await using var host = await CreateAsync();

        var result = await CheckAsync(host);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldBeHealthy_WhenTheOldestMessageWaitedLessThanTheLimit()
    {
        await using var host = await CreateAsync();
        await host.SeedAsync(Message(occurredAgo: TimeSpan.FromMinutes(1)), Message(occurredAgo: TimeSpan.FromHours(3), retryCount: 3));
        await host.Monitor.MeasureOnceAsync(CancellationToken.None);

        var result = await CheckAsync(host);

        result.Status.ShouldBe(HealthStatus.Healthy);   // a message that gave up is counted, not failed on
        result.Data["waiting"].ShouldBe(1L);
        result.Data["poison"].ShouldBe(1L);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldFail_WhenTheOldestMessageWaitsLongerThanTheLimit()
    {
        await using var host = await CreateAsync();
        await host.SeedAsync(Message(occurredAgo: TimeSpan.FromMinutes(4)));
        await host.Monitor.MeasureOnceAsync(CancellationToken.None);

        // Measured at 4 minutes; 90 s later it has waited longer than 5 (still within 3 measuring intervals).
        host.Clock.Advance(TimeSpan.FromSeconds(90));
        var result = await CheckAsync(host);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldNotBeNull().ShouldContain("1 outbox messages wait");
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldFail_WhenTheBacklogHasNotBeenMeasuredForThreeIntervals()
    {
        await using var host = await CreateAsync();
        await host.Monitor.MeasureOnceAsync(CancellationToken.None);

        host.Clock.Advance(TimeSpan.FromSeconds(91));
        var result = await CheckAsync(host);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldNotBeNull().ShouldContain("hasn't been measured");
    }

    private static Task<HealthCheckResult> CheckAsync(OutboxBacklogTestHost host)
    {
        var check = new OutboxHealthCheck<TestDbContext>(host.Monitor, MaxWaitingAge, host.Clock);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("outbox", check, HealthStatus.Degraded, tags: null),
        };
        return check.CheckHealthAsync(context);
    }
}
