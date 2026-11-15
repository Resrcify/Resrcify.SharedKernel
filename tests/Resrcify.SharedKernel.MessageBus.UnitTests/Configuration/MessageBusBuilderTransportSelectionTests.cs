using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Diagnostics;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Configuration;

/// <summary>
/// <see cref="MessageBusBuilder.UseTransportFromConfiguration"/>, row by row: a <c>MessageBus</c> section (any key) means
/// RabbitMQ in every environment; no section means RabbitMQ in Production (whose start then fails, naming the key) and in
/// memory elsewhere. The health check reports the transport chosen. No broker: nothing here connects.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageBusBuilderTransportSelectionTests
{
    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Staging", true, true)]
    [InlineData("Production", true, true)]
    [InlineData("Production", false, true)]
    [InlineData("Development", false, false)]
    [InlineData("Staging", false, false)]
    public void TheTransport_ShouldFollowTheSection_ThenTheEnvironment(
        string environment,
        bool section,
        bool onRabbitMq)
    {
        var builder = new MessageBusBuilder(new ServiceCollection());

        builder.UseTransportFromConfiguration(Configuration(section), Environment(environment));

        builder.Settings.IsInMemory.ShouldBe(!onRabbitMq);
        (builder.Settings.Connection is not null).ShouldBe(onRabbitMq);
        builder.Settings.TransportSelection.ShouldNotBeNull().HasSection.ShouldBe(section);
    }

    [Fact]
    public void AnyKeyAlone_ShouldBeASection()
    {
        var configuration = ConfigurationOf(new() { ["MessageBus:VirtualHost"] = "sandbox" });
        var builder = new MessageBusBuilder(new ServiceCollection());

        builder.UseTransportFromConfiguration(configuration, Environment(Environments.Staging));

        builder.Settings.IsInMemory.ShouldBeFalse();
    }

    [Fact]
    public void AnotherSection_ShouldNotBeOne()
    {
        // A service's own settings (SwgohApi's GalaxyOfHeroes:Queues) are deliberately not under MessageBus.
        var configuration = ConfigurationOf(new() { ["GalaxyOfHeroes:Queues:LeaderboardRequest:Prefetch"] = "2" });
        var builder = new MessageBusBuilder(new ServiceCollection());

        builder.UseTransportFromConfiguration(configuration, Environment(Environments.Staging));

        builder.Settings.IsInMemory.ShouldBeTrue();
    }

    [Fact]
    public void WithoutASection_InProduction_TheStart_ShouldFail_NamingTheKey()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Configuration(section: false));
        services.AddLogging();
        services.AddMessageBus(bus => bus.UseTransportFromConfiguration(
            Configuration(section: false),
            Environment(Environments.Production)));
        using var provider = services.BuildServiceProvider();

        var failure = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value);

        failure.Failures.ShouldContain("MessageBus:Host is required.");
    }

    [Fact]
    public void WithASection_TheBus_ShouldConnectWithIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Configuration(section: true));
        services.AddLogging();
        services.AddMessageBus(bus => bus.UseTransportFromConfiguration(
            Configuration(section: true),
            Environment(Environments.Development)));
        using var provider = services.BuildServiceProvider();

        var connection = provider.GetRequiredService<MessageBusSettings>().Connection.ShouldNotBeNull()(provider);

        connection.Host.ShouldBe("rabbitmq.invalid");
        connection.Username.ShouldBe("guest");
    }

    [Theory]
    [InlineData(false, "in-memory")]
    [InlineData(true, "rabbitmq")]
    public async Task TheHealthCheck_ShouldReportTheTransportChosen(
        bool section,
        string transport)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Configuration(section));
        services.AddLogging();
        services.AddMessageBus(bus => bus.UseTransportFromConfiguration(
            Configuration(section),
            Environment(Environments.Development)));
        services.AddHealthChecks().AddMessageBus();
        await using var provider = services.BuildServiceProvider();

        // Not started: on RabbitMQ the broker is unreachable, which the check reports beside the transport.
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries["messagebus"].Data["transport"].ShouldBe(transport);
    }

    [Fact]
    public void CalledTwice_ShouldRegisterTheStartUpLogOnce()
    {
        var services = new ServiceCollection();

        services.AddMessageBus(bus => bus
            .UseTransportFromConfiguration(Configuration(section: false), Environment(Environments.Development))
            .UseTransportFromConfiguration(Configuration(section: false), Environment(Environments.Development)));

        services
            .Count(descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(MessageBusTransportStartupLog))
            .ShouldBe(1);
    }

    [Fact]
    public void NullArguments_ShouldThrow()
    {
        var builder = new MessageBusBuilder(new ServiceCollection());

        Should.Throw<ArgumentNullException>(() => builder.UseTransportFromConfiguration(null!, Environment(Environments.Development)));
        Should.Throw<ArgumentNullException>(() => builder.UseTransportFromConfiguration(Configuration(section: false), null!));
    }

    private static IConfiguration Configuration(bool section)
        => section
            ? ConfigurationOf(new()
            {
                ["MessageBus:Host"] = "rabbitmq.invalid",
                ["MessageBus:Username"] = "guest",
                ["MessageBus:Password"] = "guest",
            })
            : ConfigurationOf([]);

    private static IConfiguration ConfigurationOf(Dictionary<string, string?> settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

    private static HostingEnvironment Environment(string name)
        => new() { EnvironmentName = name };
}
