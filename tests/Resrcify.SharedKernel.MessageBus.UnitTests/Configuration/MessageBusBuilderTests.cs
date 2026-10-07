using Rebus.Transport.InMem;
using System.Linq;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Config;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Shouldly;
using Xunit;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Configuration;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageBusBuilderTests
{
    private static readonly RabbitMqConnection Connection = new("localhost", 5672, "guest", "guest");

    [Fact]
    public void AddScatterGather_ShouldStartTheReplyBusBeforeTheHostedServicesRegisteredEarlier()
    {
        // The outbox lanes, registered first, gather through the reply bus: it must be up before them and stop after.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHostedService<EarlierHostedService>();

        services.AddMessageBus(bus => bus.UseInMemory(new InMemNetwork()).AddScatterGather());

        var hosted = services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToList();
        hosted.IndexOf(hosted.Single(descriptor => descriptor.ImplementationType == typeof(EarlierHostedService)))
            .ShouldBeGreaterThan(0);
        using var provider = services.BuildServiceProvider();
        provider.GetServices<IHostedService>().First().ShouldBeOfType<Resrcify.SharedKernel.MessageBus.ScatterGather.ScatterGatherTransport>();
    }

    private sealed class EarlierHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Fact]
    public void WithInputQueue_ShouldGiveTheServiceItsOwnErrorQueue_WhenSet()
    {
        var builder = new MessageBusBuilder(new ServiceCollection());

        builder.WithInputQueue("shardmanagement");

        builder.Settings.ErrorQueue.ShouldBe("shardmanagement.error");
    }

    [Fact]
    public void UseInMemory_ShouldReplaceRabbitMq_WhenCalledAfterIt()
    {
        var builder = new MessageBusBuilder(new ServiceCollection());

        builder.UseRabbitMq(Connection).UseInMemory();

        builder.Settings.IsInMemory.ShouldBeTrue();
        builder.Settings.Connection.ShouldBeNull();
    }

    [Fact]
    public void AddMessageBus_ShouldUseTheDefaultConfigurationStrategy_WhenNoneIsSet()
    {
        var services = new ServiceCollection();
        services.AddMessageBus(bus => bus.UseRabbitMq(Connection));
        using var provider = services.BuildServiceProvider();

        var strategy = provider.GetRequiredService<MessageBusSettings>().ResolveConfigurationStrategy(provider);

        strategy.ShouldBe(DefaultBusConfigurationStrategy.Instance);
    }

    [Fact]
    public void UseConfigurationStrategy_ShouldResolveTheStrategyFromTheProvider_WhenGivenAFactory()
    {
        var registered = new NoOpStrategy();
        var services = new ServiceCollection();
        services.AddSingleton(registered);
        services.AddMessageBus(bus => bus
            .UseRabbitMq(Connection)
            .UseConfigurationStrategy(provider => provider.GetRequiredService<NoOpStrategy>()));
        using var provider = services.BuildServiceProvider();

        var strategy = provider.GetRequiredService<MessageBusSettings>().ResolveConfigurationStrategy(provider);

        strategy.ShouldBeSameAs(registered);
    }

    [Fact]
    public void ResolveJsonOptions_ShouldUseWebDefaults_WhenNoSerializationStrategyIsSet()
    {
        var (settings, provider) = Build(bus => { });
        using var _ = provider;

        var options = settings.ResolveJsonOptions(provider);

        options.PropertyNamingPolicy.ShouldBe(JsonNamingPolicy.CamelCase);
        options.PropertyNameCaseInsensitive.ShouldBeTrue();
    }

    [Fact]
    public void UseSerializationStrategy_ShouldKeepMembersOffTheWire_WhenTheStrategyRemovesThem()
    {
        var (settings, provider) = Build(bus => bus.UseSerializationStrategy(new WithoutSecretsStrategy()));
        using var _ = provider;

        var json = JsonSerializer.Serialize(new Profile("Han", "hunter2"), settings.ResolveJsonOptions(provider));

        json.ShouldBe("""{"name":"Han"}""");
    }

    [Fact]
    public void ResolveJsonOptions_ShouldRunTheStrategyOnce_WhenEveryBusAsks()
    {
        var strategy = new CountingStrategy();
        var (settings, provider) = Build(bus => bus.UseSerializationStrategy(strategy));
        using var _ = provider;

        var first = settings.ResolveJsonOptions(provider);
        var second = settings.ResolveJsonOptions(provider);

        second.ShouldBeSameAs(first);
        strategy.Calls.ShouldBe(1);
        first.IsReadOnly.ShouldBeTrue();
    }

    private static (MessageBusSettings Settings, ServiceProvider Provider) Build(Action<MessageBusBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddMessageBus(bus => configure(bus.UseRabbitMq(Connection)));
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<MessageBusSettings>(), provider);
    }

    private sealed record Profile(string Name, string Secret);

    private sealed class WithoutSecretsStrategy : IMessageSerializationStrategy
    {
        public void Configure(JsonSerializerOptions options)
            => options.TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    typeInfo =>
                    {
                        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
                            if (typeInfo.Properties[i].Name == "secret")
                                typeInfo.Properties.RemoveAt(i);
                    },
                },
            };
    }

    private sealed class CountingStrategy : IMessageSerializationStrategy
    {
        public int Calls { get; private set; }

        public void Configure(JsonSerializerOptions options)
            => Calls++;
    }

    private sealed class NoOpStrategy : IBusConfigurationStrategy
    {
        public void ConfigureTransport(RabbitMqOptionsBuilder transport)
        {
            // Only its identity matters here.
        }

        public void ConfigureOptions(OptionsConfigurer options)
        {
            // Only its identity matters here.
        }
    }
}
