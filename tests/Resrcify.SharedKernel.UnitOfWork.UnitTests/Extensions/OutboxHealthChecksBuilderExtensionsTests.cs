using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Health;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxHealthChecksBuilderExtensionsTests
{
    [Fact]
    public async Task AddOutbox_ShouldBeAReadinessCheck_WhenGivenNoTags()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddOutbox<TestDbContext>();
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .ShouldHaveSingleItem()
            .Tags.ShouldBe([HealthTags.Ready]);
    }

    [Fact]
    public void AddOutbox_ShouldFailAsDegraded_ByDefault()
    {
        // Unhealthy on /health/ready would take every replica out of service at once over a backlog.
        var registration = Registration(builder => builder.AddOutbox<TestDbContext>(tags: ["ready"]));

        registration.FailureStatus.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public void AddOutbox_ShouldFailWithTheGivenStatus_WhenOneIsGiven()
        => Registration(builder => builder.AddOutbox<TestDbContext>(failureStatus: HealthStatus.Unhealthy))
            .FailureStatus.ShouldBe(HealthStatus.Unhealthy);

    [Fact]
    public async Task AddOutbox_ShouldCheckTheBacklog_WhenOnlyTheBareProcessingJobIsRegistered()
    {
        // Shard, Tournament and Sentinel got a 500 on /health here: the bare job didn't register the backlog monitor.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(quartz => quartz.AddProcessOutboxMessagesJob<TestDbContext>());
        services.AddHealthChecks().AddOutbox<TestDbContext>();
        await using var provider = services.BuildServiceProvider();

        var result = await CheckAsync(provider);

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldNotBeNull().ShouldContain("hasn't been measured yet");
    }

    [Fact]
    public async Task AddOutbox_ShouldReportTheMissingCall_InsteadOfThrowing_WhenNoOutboxProcessingIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddOutbox<TestDbContext>();
        await using var provider = services.BuildServiceProvider();

        var result = await CheckAsync(provider);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldNotBeNull().ShouldContain("AddOutboxProcessing<TestDbContext>");
        result.Description.ShouldContain("AddProcessOutboxMessagesJob<TestDbContext>");
    }

    [Fact]
    public async Task AddOutbox_ShouldFailTheHostsStart_WhenNoOutboxProcessingIsRegistered()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddHealthChecks().AddOutbox<TestDbContext>();
        using var host = builder.Build();

        var thrown = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        thrown.Message.ShouldContain("AddOutboxProcessing<TestDbContext>");
    }

    [Fact]
    public async Task AddOutbox_ShouldLetTheHostStart_WithTheBareProcessingJob()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddQuartz(quartz => quartz.AddProcessOutboxMessagesJob<TestDbContext>());
        builder.Services.AddHealthChecks().AddOutbox<TestDbContext>();
        // No database: the monitor's measurements fail (logged), which doesn't concern the start.
        builder.Services.AddDbContext<TestDbContext>(options => options.UseInMemoryDatabase(nameof(AddOutbox_ShouldLetTheHostStart_WithTheBareProcessingJob)));
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        host.Services.GetServices<IHostedService>().OfType<OutboxBacklogMonitor<TestDbContext>>().ShouldHaveSingleItem();
    }

    [Fact]
    public void AddOutboxProcessing_ShouldRunOneBacklogMonitor_WithItsOwnInterval_AlongsideTheJobsRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(quartz => quartz.AddProcessOutboxMessagesJob<TestDbContext>());
        services.AddOutboxProcessing<TestDbContext>(
            new SystemTextJsonOutboxSerializer(),
            outbox => outbox.BacklogCheckInterval = TimeSpan.FromSeconds(90));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().OfType<OutboxBacklogMonitor<TestDbContext>>().ShouldHaveSingleItem();
        provider.GetRequiredService<OutboxBacklogMonitor<TestDbContext>>().Interval.ShouldBe(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void AddProcessOutboxMessagesJob_ShouldMeasureTheBacklogEveryThirtySeconds()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(quartz => quartz.AddProcessOutboxMessagesJob<TestDbContext>());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<OutboxBacklogMonitor<TestDbContext>>().Interval.ShouldBe(TimeSpan.FromSeconds(30));
    }

    private static Task<HealthCheckResult> CheckAsync(ServiceProvider provider)
    {
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.ShouldHaveSingleItem();
        return registration.Factory(provider).CheckHealthAsync(new HealthCheckContext { Registration = registration });
    }

    private static HealthCheckRegistration Registration(System.Action<IHealthChecksBuilder> add)
    {
        var services = new ServiceCollection();
        add(services.AddHealthChecks());
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.ShouldHaveSingleItem();
    }
}
