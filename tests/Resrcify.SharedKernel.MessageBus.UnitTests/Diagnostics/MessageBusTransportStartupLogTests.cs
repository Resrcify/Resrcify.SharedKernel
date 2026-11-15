using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Diagnostics;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Resrcify.SharedKernel.MessageBus.Testing;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Diagnostics;

/// <summary>
/// The line the start logs about the transport <c>UseTransportFromConfiguration</c> chose: RabbitMQ and its broker as
/// information, never the password; in memory as information in Development and as a warning anywhere else, worded for
/// a bus that only publishes or for one that receives; nothing once the bus was given another transport.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageBusTransportStartupLogTests
{
    private const string Password = "never-logged-password";

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task OnRabbitMq_ShouldLogTheBroker_AsInformation_NeverThePassword(string environment)
    {
        var entries = await StartAsync(environment, section: true);

        var (level, message) = entries.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Information);
        message.ShouldBe("Message bus: RabbitMQ at rabbitmq.invalid:5672, virtual host / (the MessageBus section)");
        message.ShouldNotContain(Password);
    }

    [Fact]
    public async Task InMemory_PublishingOnly_InDevelopment_ShouldLogIt_AsInformation()
    {
        var entries = await StartAsync(Environments.Development, section: false);

        entries.ShouldHaveSingleItem().ShouldBe((
            LogLevel.Information,
            "Message bus: in memory (no MessageBus section): nothing it publishes leaves this process"));
    }

    [Fact]
    public async Task InMemory_PublishingOnly_OutsideDevelopment_ShouldWarn()
    {
        var entries = await StartAsync(Environments.Staging, section: false);

        entries.ShouldHaveSingleItem().ShouldBe((
            LogLevel.Warning,
            "Message bus: in memory (no MessageBus section) in Staging: nothing it publishes leaves this process. "
                + "Set MessageBus__Host, MessageBus__Username and MessageBus__Password to publish on RabbitMQ"));
    }

    [Fact]
    public async Task InMemory_WithAnInputQueue_InDevelopment_ShouldSayNothingReachesIt()
    {
        var entries = await StartAsync(Environments.Development, section: false, bus => bus.WithInputQueue("shardmanagement"));

        entries.ShouldHaveSingleItem().ShouldBe((
            LogLevel.Information,
            "Message bus: in memory (no MessageBus section): nothing outside this process reaches its queues, and nothing it sends or publishes leaves it"));
    }

    [Fact]
    public async Task InMemory_WithARateLimitedQueue_OutsideDevelopment_ShouldWarn_ThatNothingReachesIt()
    {
        var entries = await StartAsync(
            Environments.Staging,
            section: false,
            bus => bus.Services.AddSingleton<IQueueConsumer>(new Queue("PlayerArenaProfileRequest")));

        entries.ShouldHaveSingleItem().ShouldBe((
            LogLevel.Warning,
            "Message bus: in memory (no MessageBus section) in Staging: nothing outside this process reaches its queues, "
                + "and nothing it sends or publishes leaves it. "
                + "Set MessageBus__Host, MessageBus__Username and MessageBus__Password to run it on RabbitMQ"));
    }

    [Fact]
    public async Task GivenAnotherTransportAfterwards_ShouldLogNothing()
    {
        var entries = await StartAsync(Environments.Staging, section: true, bus => bus.UseInMemory());

        entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task SwitchedByTheTestHarness_ShouldLogNothing()
    {
        var entries = await StartAsync(
            Environments.Development,
            section: true,
            configureServices: services => services.AddMessageBusTestHarness());

        entries.ShouldBeEmpty();
    }

    private static async Task<List<(LogLevel Level, string Message)>> StartAsync(
        string environment,
        bool section,
        Action<MessageBusBuilder>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var configuration = Configuration(section);
        using var logs = new FakeLoggerProvider();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddMessageBus(bus =>
        {
            bus.UseTransportFromConfiguration(configuration, new HostingEnvironment { EnvironmentName = environment });
            configure?.Invoke(bus);
        });
        configureServices?.Invoke(services);
        await using var provider = services.BuildServiceProvider();

        // Only the start-up log: the bus itself isn't started (no broker).
        foreach (var log in provider.GetServices<IHostedService>().OfType<MessageBusTransportStartupLog>())
            await log.StartAsync(CancellationToken.None);

        return [.. logs.Collector.GetSnapshot()
            .Where(entry => entry.Category == typeof(MessageBusTransportStartupLog).FullName)
            .Select(entry => (entry.Level, entry.Message))];
    }

    private static IConfiguration Configuration(bool section)
    {
        var settings = new Dictionary<string, string?>();
        if (section)
        {
            settings["MessageBus:Host"] = "rabbitmq.invalid";
            settings["MessageBus:Username"] = "guest";
            settings["MessageBus:Password"] = Password;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private sealed class Queue(string queueName) : IQueueConsumer
    {
        public string QueueName => queueName;

        public bool IsConsuming => true;
    }
}
