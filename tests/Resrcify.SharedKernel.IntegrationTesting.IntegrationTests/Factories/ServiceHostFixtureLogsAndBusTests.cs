using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.IntegrationTesting.TestService;
using Resrcify.SharedKernel.MessageBus.Testing;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Factories;

/// <summary>
/// What a test reads from a hosted service without a fixture of its own: what it logged (through the logging abstraction
/// or Serilog), what its buses published on the test harness, and what reached a real broker.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ServiceHostFixtureLogsAndBusTests(
    FullServiceHostFixture full,
    SerilogServiceHostFixture serilog,
    HarnessServiceHostFixture harness)
    : IClassFixture<FullServiceHostFixture>,
    IClassFixture<SerilogServiceHostFixture>,
    IClassFixture<HarnessServiceHostFixture>
{
    // The category the service logs under: its (internal) Greeter's.
    private const string GreeterCategory = "Resrcify.SharedKernel.IntegrationTesting.TestService.Greeter";

    [Fact]
    public async Task Logs_ShouldHoldWhatTheServiceLogged_ThroughTheLoggingAbstraction()
    {
        var message = $"plain-{Guid.NewGuid():N}";
        using var client = full.CreateClient();

        using var response = await client.PostAsync(new Uri($"/log/{message}", UriKind.Relative), null);

        response.EnsureSuccessStatusCode();
        var logged = full.Logs.GetSnapshot().Where(record => record.Message == $"Logged {message}").ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Information);
        logged.Category.ShouldBe(GreeterCategory);
    }

    [Fact]
    public async Task Logs_ShouldHoldWhatTheServiceLogged_ThroughSerilog_Once()
    {
        var message = $"serilog-{Guid.NewGuid():N}";
        using var client = serilog.CreateClient();

        using var response = await client.PostAsync(new Uri($"/log/{message}", UriKind.Relative), null);

        response.EnsureSuccessStatusCode();
        var logged = serilog.Logs.GetSnapshot().Where(record => record.Message == $"Logged {message}").ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Information);
        logged.Category.ShouldBe(GreeterCategory);
        logged.GetStructuredStateValue("Message").ShouldBe(message);
    }

    [Fact]
    public async Task RenderedLogs_ShouldHoldTheLineAsProductionPrintsIt_WithItsStringPropertyQuoted()
    {
        var message = $"rendered-{Guid.NewGuid():N}";
        using var client = serilog.CreateClient();

        using var response = await client.PostAsync(new Uri($"/log/{message}", UriKind.Relative), null);

        response.EnsureSuccessStatusCode();
        var line = serilog.RenderedLogs.Lines.Where(line => line.Template == "Logged {Message}" && line.Message.Contains(message, StringComparison.Ordinal)).ShouldHaveSingleItem();
        line.Message.ShouldBe($"Logged \"{message}\"");
        line.Level.ShouldBe(Serilog.Events.LogEventLevel.Information);
    }

    [Fact]
    public async Task RenderedLogs_ShouldNotHoldALine_TheServiceLoggedWithoutSerilog()
    {
        var message = $"plain-rendered-{Guid.NewGuid():N}";
        using var client = full.CreateClient();

        using var response = await client.PostAsync(new Uri($"/log/{message}", UriKind.Relative), null);

        response.EnsureSuccessStatusCode();
        full.RenderedLogs.Lines.ShouldNotContain(line => line.Message.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Harness_ShouldRecordWhatTheServicePublished_WithoutABroker()
    {
        var name = $"harness-{Guid.NewGuid():N}";
        using var client = harness.CreateClient();

        using var response = await client.PostAsync(new Uri($"/greeted/{name}", UriKind.Relative), null);

        response.EnsureSuccessStatusCode();
        harness.RabbitMq.ShouldBeNull();
        (await harness.Harness.Published.WaitForAsync<Greeted>(greeted => greeted.Name == name)).Name.ShouldBe(name);
    }

    [Fact]
    public void Harness_ShouldSaySo_WhenTheFixtureUsesItsBroker()
        => Should.Throw<InvalidOperationException>(() => full.Harness)
            .Message.ShouldContain("UsesMessageBusTestHarness");

    [Fact]
    public async Task PublishedEventListener_ShouldReceiveWhatTheServicePublished_OnTheRealBroker()
    {
        var name = $"broker-{Guid.NewGuid():N}";
        await using var events = await PublishedEventListener.StartAsync(
            full.RabbitMq!.Connection,
            listen => listen.Listen<Greeted>());
        using var client = full.CreateClient();

        using var response = await client.PostAsync(new Uri($"/greeted/{name}", UriKind.Relative), null);

        response.EnsureSuccessStatusCode();
        var received = await events.Received.WaitForAsync<Greeted>(greeted => greeted.Name == name, TimeSpan.FromSeconds(30));
        received.Name.ShouldBe(name);
        events.Received.All.ShouldContain(recorded => recorded.MessageId != null);
    }

    [Fact]
    public async Task ProductionQueueDefaults_ShouldMakeAQueueDeclaredWithoutATypeAQuorumQueue()
    {
        await using var events = await PublishedEventListener.StartAsync(
            full.RabbitMq!.Connection,
            listen => listen.Listen<Greeted>());

        var queues = await full.RabbitMq.QueuesAsync();

        queues.ShouldContain(new RabbitMqQueue(events.InputQueue, "quorum", Exclusive: false));
    }

    [Fact]
    public async Task AddQuorumVirtualHostAsync_ShouldGiveTheVirtualHostQuorumQueuesAndProductionsDeliveryLimit()
    {
        var connection = await full.RabbitMq!.AddQuorumVirtualHostAsync();
        await using var events = await PublishedEventListener.StartAsync(connection, listen => listen.Listen<Greeted>());

        var queues = await full.RabbitMq.QueuesAsync(connection.VirtualHost);
        var policies = await full.RabbitMq.Container.ExecAsync(
            ["rabbitmqctl", "list_operator_policies", "-p", connection.VirtualHost, "--quiet", "--no-table-headers"]);

        queues.ShouldContain(queue => queue.Name == events.InputQueue && queue.Type == "quorum");
        policies.Stdout.ShouldContain($"\"delivery-limit\":{RabbitMqContainerFixture.ProductionDeliveryLimit}");
    }
}
