using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Diagnostics;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Diagnostics;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageBusHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ShouldBeHealthy_WhenTheBusRunsInMemory()
    {
        var settings = new MessageBusSettings();
        settings.UseInMemory(new Rebus.Transport.InMem.InMemNetwork());

        var result = await CheckAsync(settings, new ServiceCollection());

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data["transport"].ShouldBe("in-memory");
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReportTheFailureStatus_WhenRabbitMqIsUnreachable()
    {
        var settings = new MessageBusSettings();
        settings.UseRabbitMq(_ => new RabbitMqConnection("localhost", 1, "guest", "guest"));

        var result = await CheckAsync(settings, new ServiceCollection(), HealthStatus.Degraded);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Data["broker"].ShouldBe("unreachable");
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldListEachQueue_WhetherItConsumesOrSteppedAside()
    {
        var settings = new MessageBusSettings();
        settings.UseInMemory(new Rebus.Transport.InMem.InMemNetwork());
        var services = new ServiceCollection();
        services.AddSingleton<IQueueConsumer>(new Queue("swgohapi.guild", isConsuming: true));
        services.AddSingleton<IQueueConsumer>(new Queue("swgohapi.leaderboard", isConsuming: false));

        var result = await CheckAsync(settings, services);

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data["swgohapi.guild"].ShouldBe("consuming");
        result.Data["swgohapi.leaderboard"].ShouldBe("stepped aside");
    }

    private static async Task<HealthCheckResult> CheckAsync(
        MessageBusSettings settings,
        ServiceCollection services,
        HealthStatus failureStatus = HealthStatus.Unhealthy)
    {
        await using var provider = services.BuildServiceProvider();
        var check = new MessageBusHealthCheck(settings, provider);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("messagebus", check, failureStatus, tags: null),
        };
        return await check.CheckHealthAsync(context);
    }

    private sealed class Queue(string queueName, bool isConsuming) : IQueueConsumer
    {
        public string QueueName => queueName;

        public bool IsConsuming => isConsuming;
    }
}
