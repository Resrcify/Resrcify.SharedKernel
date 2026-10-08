using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Rebus.Config;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Configuration;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class RabbitMqConnectionTests
{
    [Fact]
    public void ToString_ShouldNotShowThePassword()
    {
        // A record prints every property: logged, the password (and the connection string holding it) leaked.
        var printed = new RabbitMqConnection("rabbitmq", 5672, "shard", "s3cr3t-p4ss") { VirtualHost = "titan" }.ToString();

        printed.ShouldNotContain("s3cr3t-p4ss");
        printed.ShouldContain("Password = ***");
        printed.ShouldContain("Host = rabbitmq");
        printed.ShouldContain("VirtualHost = titan");
    }

    [Fact]
    public void ConnectionString_ShouldUseTheDefaultVirtualHost_WhenNoneIsSet()
        => new RabbitMqConnection("rabbit", 5672, "user", "p@ss").ConnectionString
            .ShouldBe("amqp://user:p%40ss@rabbit:5672/%2F");

    [Fact]
    public void ConnectionString_ShouldCarryTheVirtualHostAndTls_WhenSet()
        => new RabbitMqConnection("rabbit", 5671, "user", "secret") { VirtualHost = "svc", UseTls = true }.ConnectionString
            .ShouldBe("amqps://user:secret@rabbit:5671/svc");

    [Fact]
    public void CreateConnectionFactory_ShouldConnectAsTheBusesDo_WithTheStrategysSettings()
    {
        // The watcher's own connection: it used to take only host, port and credentials, so a service on a virtual
        // host of its own, or on TLS, was reported unreachable forever.
        var settings = new MessageBusSettings();
        settings.UseRabbitMq(_ => new RabbitMqConnection("rabbit", 5671, "user", "secret") { VirtualHost = "svc", UseTls = true });
        settings.ConfigurationStrategy = _ => new TimeoutStrategy();
        using var provider = new ServiceCollection().BuildServiceProvider();

        var factory = settings.CreateConnectionFactory(provider, "watcher");

        factory.VirtualHost.ShouldBe("svc");
        factory.Ssl.Enabled.ShouldBeTrue();
        factory.HostName.ShouldBe("rabbit");
        factory.RequestedConnectionTimeout.ShouldBe(TimeSpan.FromSeconds(7));
        factory.ClientProvidedName.ShouldBe("watcher");
    }

    private sealed class TimeoutStrategy : IBusConfigurationStrategy
    {
        public void ConfigureTransport(RabbitMqOptionsBuilder transport)
        {
        }

        public void ConfigureOptions(OptionsConfigurer options)
        {
        }

        public void ConfigureConnectionFactory(ConnectionFactory factory)
            => factory.RequestedConnectionTimeout = TimeSpan.FromSeconds(7);
    }
}
