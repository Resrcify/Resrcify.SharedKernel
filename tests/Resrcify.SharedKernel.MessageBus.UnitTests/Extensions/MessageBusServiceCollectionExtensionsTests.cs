using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Extensions;

/// <summary>The service's own error queue, and a configuration strategy's retry settings on top of it.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageBusServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddMessageBus_ShouldKeepTheServicesErrorQueue_WhenAStrategyChangesTheDeliveryAttempts()
    {
        var queue = $"subscriber-{Guid.NewGuid():N}";

        var (network, log) = await PublishAFailingEventAsync(queue, new RetryStrategy(maxDeliveryAttempts: 2, errorQueue: null), $"{queue}.error");

        network.Count($"{queue}.error").ShouldBe(1);
        log.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task AddMessageBus_ShouldUseTheStrategysErrorQueue_WhenTheStrategyNamesOne()
    {
        var queue = $"subscriber-{Guid.NewGuid():N}";
        var errorQueue = $"errors-{Guid.NewGuid():N}";

        var (network, _) = await PublishAFailingEventAsync(queue, new RetryStrategy(maxDeliveryAttempts: 2, errorQueue), errorQueue);

        network.Count(errorQueue).ShouldBe(1);
        network.Count($"{queue}.error").ShouldBe(0);
    }

    /// <summary>Publishes an event whose handler always throws, and waits until it reaches <paramref name="expectedErrorQueue"/>.</summary>
    private static async Task<(InMemNetwork Network, PayoutLog Log)> PublishAFailingEventAsync(
        string queue,
        IBusConfigurationStrategy strategy,
        string expectedErrorQueue)
    {
        var network = new InMemNetwork();
        var log = new PayoutLog { AlwaysFail = true };
        using var subscriber = await InMemoryServices.StartAsync(
            network,
            bus => bus
                .WithInputQueue(queue)
                .UseConfigurationStrategy(strategy)
                .AddEventHandlers(typeof(MessageBusServiceCollectionExtensionsTests).Assembly),
            services => services.AddSingleton(log));
        using var publisher = await InMemoryServices.StartAsync(network, _ => { });

        await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new PayoutRotated("shard-1", 1));
        await InMemoryServices.WaitUntilAsync(() => network.Count(expectedErrorQueue) > 0);
        return (network, log);
    }

    private sealed class RetryStrategy(int maxDeliveryAttempts, string? errorQueue) : IBusConfigurationStrategy
    {
        public void ConfigureTransport(RabbitMqOptionsBuilder transport)
        {
            // In memory: no RabbitMQ transport to configure.
        }

        public void ConfigureOptions(OptionsConfigurer options)
        {
            if (errorQueue is null)
                options.RetryStrategy(maxDeliveryAttempts: maxDeliveryAttempts);
            else
                options.RetryStrategy(errorQueueName: errorQueue, maxDeliveryAttempts: maxDeliveryAttempts);
        }
    }
}
