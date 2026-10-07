using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.PublishSubscribe;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class EventSubscriptionsTests
{
    [Fact]
    public void HostedServices_ShouldNotStartTheBus_WhenTheHostMakesThem()
    {
        // The host makes every hosted service before any starts (and before migrations run at StartingAsync): making
        // the subscriptions mustn't build the bus, which would consume events against a schema not migrated yet.
        var strategy = new CountingStrategy();
        var services = new ServiceCollection();
        services.AddLogging().AddSingleton(new PayoutLog());
        services.AddMessageBus(bus => bus
            .UseInMemory(new InMemNetwork())
            .UseConfigurationStrategy(strategy)
            .WithInputQueue($"subscriber-{Guid.NewGuid():N}")
            .AddEventHandlers(typeof(EventSubscriptionsTests).Assembly));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ShouldNotBeEmpty();

        strategy.BusesConfigured.ShouldBe(0);
    }

    private sealed class CountingStrategy : Resrcify.SharedKernel.MessageBus.Abstractions.IBusConfigurationStrategy
    {
        public int BusesConfigured { get; private set; }

        public void ConfigureTransport(Rebus.Config.RabbitMqOptionsBuilder transport)
        {
        }

        public void ConfigureOptions(Rebus.Config.OptionsConfigurer options)
            => BusesConfigured++;
    }

    [Fact]
    public async Task StartAsync_ShouldUnsubscribe_WhenTheServiceRemovesASubscription()
    {
        var network = new InMemNetwork();
        var queue = $"subscriber-{Guid.NewGuid():N}";
        using (await InMemoryServices.StartAsync(
            network,
            bus => bus.WithInputQueue(queue).AddEventHandlers(typeof(EventSubscriptionsTests).Assembly),
            services => services.AddSingleton(new PayoutLog())))
        {
            network.GetSubscribers(nameof(PayoutRotated)).ShouldContain(queue);
        }

        // The next release: the handler is gone, the subscription is removed with it.
        using var next = await InMemoryServices.StartAsync(network, bus => bus.WithInputQueue(queue).RemoveSubscription<PayoutRotated>());
        using var publisher = await InMemoryServices.StartAsync(network, _ => { });
        await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new PayoutRotated("shard-1", 1));

        network.GetSubscribers(nameof(PayoutRotated)).ShouldNotContain(queue);
        network.Count(queue).ShouldBe(0);
    }

    [Fact]
    public async Task StartAsync_ShouldFail_WhenTheServiceSkipsDuplicatesWithoutACachingService()
    {
        var failure = await Should.ThrowAsync<InvalidOperationException>(() => InMemoryServices.StartAsync(
            new InMemNetwork(),
            bus => bus.WithInputQueue("subscriber").AddEventHandlers(typeof(EventSubscriptionsTests).Assembly).SkipDuplicateEvents()));

        failure.Message.ShouldContain("ICachingService");
    }
}
