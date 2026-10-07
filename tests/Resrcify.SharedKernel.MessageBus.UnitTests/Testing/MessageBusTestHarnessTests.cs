using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rebus.Bus;
using Rebus.Handlers;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.Testing;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Testing;

/// <summary>
/// The harness on a service configured for RabbitMQ (an address nothing listens on): every test also shows the switch
/// to the in-memory network.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageBusTestHarnessTests
{
    [Fact]
    public async Task Sent_AndConsumed_ShouldRecordAMessageSentToTheServicesQueue()
    {
        using var host = await StartAsync(new MessageBusTestNetwork());
        var harness = Harness(host);

        await host.Services.GetRequiredService<IBus>().Send(new OrderPlaced("o1"));

        (await harness.Consumed.WaitForAsync<OrderPlaced>()).Id.ShouldBe("o1");
        var sent = harness.Sent.All.ShouldHaveSingleItem();
        sent.Message.ShouldBe(new OrderPlaced("o1"));
        sent.Destinations.ShouldBe(["orders"]);
        harness.Faulted.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task Published_ShouldCompleteAWaitStartedBeforeTheEventWasPublished()
    {
        using var host = await StartAsync(new MessageBusTestNetwork());
        var harness = Harness(host);
        var waiting = harness.Published.WaitForAsync<PlayerRenamed>(renamed => renamed.Name == "Han");

        await host.Services.GetRequiredService<IEventBus>().PublishAsync(new PlayerRenamed("p1", "Leia"));
        await host.Services.GetRequiredService<IEventBus>().PublishAsync(new PlayerRenamed("p1", "Han"));

        (await waiting).ShouldBe(new PlayerRenamed("p1", "Han"));
        harness.Published.Of<PlayerRenamed>().Count.ShouldBe(2);
        harness.Sent.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task WaitForAsync_ShouldSayWhatWasRecorded_WhenNothingMatchesInTime()
    {
        using var host = await StartAsync(new MessageBusTestNetwork());
        await host.Services.GetRequiredService<IBus>().Send(new OrderPlaced("o1"));
        await Harness(host).Consumed.WaitForAsync<OrderPlaced>();

        var timeout = await Should.ThrowAsync<TimeoutException>(
            () => Harness(host).Consumed.WaitForAsync<OrderPlaced>(order => order.Id == "o2", TimeSpan.FromMilliseconds(100)));

        timeout.Message.ShouldContain("No matching OrderPlaced was consumed");
        timeout.Message.ShouldContain("OrderPlaced x1");
    }

    [Fact]
    public async Task FailNext_ShouldFailTheNextDeliveries_ThenLetTheRetryThrough()
    {
        using var host = await StartAsync(new MessageBusTestNetwork());
        var harness = Harness(host);
        harness.FailNext<OrderPlaced>(deliveries: 2);

        await host.Services.GetRequiredService<IBus>().Send(new OrderPlaced("o1"));

        await harness.Consumed.WaitForAsync<OrderPlaced>();
        harness.Faulted.All.Count.ShouldBe(2);
        harness.Faulted.All.ShouldAllBe(faulted => faulted.Exception is InvalidOperationException && faulted.Queue == "orders");
        host.Services.GetRequiredService<Orders>().Placed.ShouldBe(["o1"]);
    }

    [Fact]
    public async Task DeadLettered_ShouldRecordTheMessageAndItsLastFailure_WhenEveryTryFails()
    {
        var network = new MessageBusTestNetwork();
        using var host = await StartAsync(network);
        var harness = Harness(host);
        harness.FailNext<OrderPlaced>(deliveries: 100, new InvalidOperationException("the warehouse is down"));

        await host.Services.GetRequiredService<IBus>().Send(new OrderPlaced("o1"));

        var deadLettered = await harness.DeadLettered.WaitForAsync<OrderPlaced>();
        deadLettered.Id.ShouldBe("o1");
        harness.DeadLettered.All.ShouldHaveSingleItem().Exception!.Message.ShouldBe("the warehouse is down");
        harness.Consumed.All.ShouldBeEmpty();
        // The error queue holds it, and isn't waited for.
        await network.WaitUntilIdleAsync();
        network.Network.GetCount("orders.error").ShouldBe(1);
    }

    [Fact]
    public async Task FailStart_ShouldFailTheStartOfTheBusOnTheQueue()
    {
        using var host = Build(new MessageBusTestNetwork());
        Harness(host).FailStart("orders");

        // Rebus wraps it (a ResolutionException) when the bus is the service's own.
        var failure = await Should.ThrowAsync<Exception>(() => host.StartAsync());

        failure.ToString().ShouldContain("'orders' failed to start");
    }

    [Fact]
    public void AddMessageBusTestHarness_ShouldThrow_WhenTheMessageBusIsNotAddedFirst()
        => Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddMessageBusTestHarness())
            .Message.ShouldContain("Call AddMessageBus before AddMessageBusTestHarness");

    private static MessageBusTestHarness Harness(IHost host)
        => host.Services.GetRequiredService<MessageBusTestHarness>();

    private static async Task<IHost> StartAsync(MessageBusTestNetwork network)
    {
        var host = Build(network);
        await host.StartAsync();
        return host;
    }

    private static IHost Build(MessageBusTestNetwork network)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<Orders>();
        builder.Services.AddMessageBus(bus => bus
            .UseRabbitMq(new RabbitMqConnection("rabbitmq.invalid", 5672, "guest", "guest"))
            .WithInputQueue("orders")
            .AddMessage<OrderPlaced>("test.order-placed.v1", sendTo: "orders")
            .AddMessage<PlayerRenamed>("test.player-renamed.v1")
            .AddHandler<OrderPlacedHandler>());
        builder.Services.AddMessageBusTestHarness(network);
        return builder.Build();
    }

    internal sealed record OrderPlaced(string Id);

    internal sealed record PlayerRenamed(string PlayerId, string Name);

    internal sealed class Orders
    {
        private readonly ConcurrentQueue<string> _placed = new();

        public string[] Placed
            => [.. _placed];

        public void Place(string id)
            => _placed.Enqueue(id);
    }

    internal sealed class OrderPlacedHandler(Orders orders) : IHandleMessages<OrderPlaced>
    {
        public Task Handle(OrderPlaced message)
        {
            ArgumentNullException.ThrowIfNull(message);
            orders.Place(message.Id);
            return Task.CompletedTask;
        }
    }
}
