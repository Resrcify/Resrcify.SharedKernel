using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Resrcify.SharedKernel.MessageBus.Broker;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests;

/// <summary>The message bus' health check against a real broker, and against one that can't be reached.</summary>
[Collection(BusCollection.Name)]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class MessageBusHealthCheckTests(BusFixture bus)
{
    [Fact]
    public async Task CheckHealthAsync_WhenRabbitMqIsReachable_IsHealthy()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMessageBus(messageBus => messageBus.UseRabbitMq(bus.RabbitMqConnection));
        builder.Services.AddHealthChecks().AddMessageBus();
        using var host = builder.Build();
        await host.StartAsync();

        var report = await WaitForStatusAsync(host.Services, HealthStatus.Healthy);

        report.Entries["messagebus"].Data["broker"].ShouldBe("connected");
        await host.StopAsync();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRabbitMqIsUnreachable_ReportsTheFailureStatus()
    {
        var unreachable = bus.RabbitMqConnection with { Port = 1 };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageBus(messageBus => messageBus.UseRabbitMq(unreachable));
        services.AddHealthChecks().AddMessageBus(failureStatus: HealthStatus.Unhealthy);
        await using var provider = services.BuildServiceProvider();
        // Only the broker watcher: the bus itself can't start without a broker.
        var watcher = provider.GetRequiredService<BrokerConnectionWatcher>();
        await watcher.StartAsync(default);

        await Task.Delay(TimeSpan.FromSeconds(2));
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Status.ShouldBe(HealthStatus.Unhealthy);
        report.Entries["messagebus"].Data["broker"].ShouldBe("unreachable");
        await watcher.StopAsync(default);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTheReplyBusCantRestartAfterAnOutage_ReportsItUntilItIsBack()
    {
        var strategy = new FailingStartStrategy(address => address?.Contains(".replies.", StringComparison.Ordinal) == true);
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMessageBus(messageBus => messageBus
            .UseRabbitMq(bus.RabbitMqConnection)
            .UseConfigurationStrategy(strategy)
            .AddScatterGather());
        builder.Services.AddHealthChecks().AddMessageBus(failureStatus: HealthStatus.Unhealthy);
        using var host = builder.Build();
        await host.StartAsync();
        await WaitForStatusAsync(host.Services, HealthStatus.Healthy);
        strategy.Arm(2);

        // As the broker watcher does when RabbitMQ is back; the reply queue can't be declared for the next two tries.
        host.Services.GetRequiredService<ScatterGatherTransport>().OnBrokerRecovered();

        // It used to stay Healthy (the watcher's connection was fine) while every gather failed.
        var failing = await WaitForStatusAsync(host.Services, HealthStatus.Unhealthy);
        failing.Entries["messagebus"].Data["scatter_gather"].ShouldBe("restarting");
        var back = await WaitForStatusAsync(host.Services, HealthStatus.Healthy);
        back.Entries["messagebus"].Data["scatter_gather"].ShouldBe("running");
        strategy.Failed.ShouldBe(2);
        await host.StopAsync();
    }

    private static async Task<HealthReport> WaitForStatusAsync(IServiceProvider services, HealthStatus status)
    {
        var healthChecks = services.GetRequiredService<HealthCheckService>();
        var giveUpAt = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var report = await healthChecks.CheckHealthAsync();
            if (report.Status == status || DateTime.UtcNow > giveUpAt)
                return report;
            await Task.Delay(200);
        }
    }
}
