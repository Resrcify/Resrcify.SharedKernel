using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.Testing;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Configuration;

/// <summary>
/// <c>UseRabbitMqFromConfiguration</c>: the bus binds and validates the <c>MessageBus</c> section itself, with the keys
/// the services' appsettings, Kubernetes manifests (<c>MessageBus__Host</c>, …) and <c>ServiceHostFixture</c> use.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class RabbitMqOptionsTests
{
    private static readonly Dictionary<string, string?> ServiceSettings = new()
    {
        ["MessageBus:Host"] = "messagebus",
        ["MessageBus:Port"] = "5673",
        ["MessageBus:Username"] = "messageUser",
        ["MessageBus:Password"] = "supersecretC0de",
    };

    [Fact]
    public void UseRabbitMqFromConfiguration_ShouldConnectAsTheMessageBusSectionSays()
    {
        using var host = BuildHost(ServiceSettings);

        var connection = ResolveConnection(host);

        connection.ShouldBe(new RabbitMqConnection("messagebus", 5673, "messageUser", "supersecretC0de"));
        connection.VirtualHost.ShouldBe("/");
        connection.UseTls.ShouldBeFalse();
    }

    [Fact]
    public void UseRabbitMqFromConfiguration_ShouldReadTheVirtualHostAndTls_WhenTheSectionHasThem()
    {
        using var host = BuildHost(new Dictionary<string, string?>(ServiceSettings)
        {
            ["MessageBus:VirtualHost"] = "titan",
            ["MessageBus:UseTls"] = "true",
        });

        ResolveConnection(host).ConnectionString.ShouldBe("amqps://messageUser:supersecretC0de@messagebus:5673/titan");
    }

    [Fact]
    public void UseRabbitMqFromConfiguration_ShouldReadTheKubernetesEnvironmentVariables()
    {
        var prefix = $"RESRCIFY_TEST_{Guid.NewGuid():N}_";
        var variables = new Dictionary<string, string>
        {
            ["MessageBus__Host"] = "rabbitmq.titan.svc",
            ["MessageBus__Port"] = "5672",
            ["MessageBus__Username"] = "shard",
            ["MessageBus__Password"] = "from-a-secret",
        };
        foreach (var (key, value) in variables)
            Environment.SetEnvironmentVariable(prefix + key, value);
        try
        {
            using var host = BuildHost(configure: configuration => configuration.AddEnvironmentVariables(prefix));

            ResolveConnection(host).ShouldBe(new RabbitMqConnection("rabbitmq.titan.svc", 5672, "shard", "from-a-secret"));
        }
        finally
        {
            foreach (var key in variables.Keys)
                Environment.SetEnvironmentVariable(prefix + key, null);
        }
    }

    [Fact]
    public void UseRabbitMqFromConfiguration_ShouldReadTheSectionNamed_WhenGivenOne()
    {
        using var host = BuildHost(
            new Dictionary<string, string?> { ["Bus:Host"] = "other", ["Bus:Username"] = "u", ["Bus:Password"] = "p" },
            bus => bus.UseRabbitMqFromConfiguration("Bus"));

        ResolveConnection(host).ShouldBe(new RabbitMqConnection("other", 5672, "u", "p"));
    }

    [Fact]
    public async Task StartAsync_ShouldFailNamingEachKey_WhenTheSectionIsIncomplete()
    {
        using var host = BuildHost(new Dictionary<string, string?> { ["MessageBus:Port"] = "70000" });

        var failure = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        failure.Failures.ShouldBe(
        [
            "MessageBus:Host is required.",
            "MessageBus:Username is required.",
            "MessageBus:Password is required.",
            "MessageBus:Port must be a port (1-65535); it is 70000.",
        ]);
    }

    [Fact]
    public async Task StartAsync_ShouldNotAskForTheSection_WhenTheTestHarnessRunsTheBusInMemory()
    {
        using var host = BuildHost(
            new Dictionary<string, string?>(),
            configureServices: services => services.AddMessageBusTestHarness());

        await Should.NotThrowAsync(() => host.StartAsync());
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_ShouldNotAskForTheSection_WhenTheBusIsGivenAnotherTransportAfterwards()
    {
        using var host = BuildHost(
            new Dictionary<string, string?>(),
            bus => bus.UseRabbitMqFromConfiguration().UseInMemory());

        await Should.NotThrowAsync(() => host.StartAsync());
        await host.StopAsync();
    }

    [Fact]
    public void ToConnection_ShouldPrintWithoutThePassword()
    {
        var options = new RabbitMqOptions { Host = "messagebus", Username = "messageUser", Password = "supersecretC0de" };

        var printed = options.ToConnection().ToString();

        printed.ShouldNotContain("supersecretC0de");
        printed.ShouldContain("Password = ***");
    }

    [Fact]
    public void ToConnection_ShouldUseTheDefaultVirtualHost_WhenTheSectionLeavesItBlank()
        => new RabbitMqOptions { Host = "h", Username = "u", Password = "p", VirtualHost = "" }
            .ToConnection()
            .VirtualHost
            .ShouldBe("/");

    private static RabbitMqConnection ResolveConnection(IHost host)
        => host.Services.GetRequiredService<MessageBusSettings>().ResolveConnection(host.Services);

    private static IHost BuildHost(
        Dictionary<string, string?>? settings = null,
        Action<MessageBusBuilder>? configureBus = null,
        Action<IConfigurationBuilder>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        configure?.Invoke(builder.Configuration);
        builder.Services.AddLogging();
        builder.Services.AddMessageBus(configureBus ?? (bus => bus.UseRabbitMqFromConfiguration()));
        configureServices?.Invoke(builder.Services);
        return builder.Build();
    }
}
