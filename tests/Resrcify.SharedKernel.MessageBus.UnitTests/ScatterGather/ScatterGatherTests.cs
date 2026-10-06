using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Shouldly;
using Xunit;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ScatterGatherTests
{
    [Fact]
    public void Gathered_ShouldSplitResultsFailuresAndUnanswered_WhenSomeItemsAreMissing()
    {
        var gathered = new Gathered<Pong>(
            ["a", "b", "c"],
            new Dictionary<string, Pong> { ["a"] = new("hi") },
            new Dictionary<string, IReadOnlyList<Error>> { ["b"] = [NoData] });

        gathered.Results.Keys.ShouldBe(["a"]);
        gathered.Failures.Keys.ShouldBe(["b"]);
        gathered.UnansweredKeys.ShouldBe(["c"]);
        gathered.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void Gathered_ShouldGiveEachItemAsAResult_WhenIndexedByKey()
    {
        var gathered = new Gathered<Pong>(
            ["a", "b", "c"],
            new Dictionary<string, Pong> { ["a"] = new("hi") },
            new Dictionary<string, IReadOnlyList<Error>> { ["b"] = [NoData] });

        gathered["a"].Value.ShouldBe(new Pong("hi"));
        gathered["b"].Errors.ShouldBe([NoData]);
        gathered["c"].Errors.Single().Type.ShouldBe(ErrorType.Timeout);
        gathered["c"].Errors.Single().Code.ShouldBe("ScatterGather.Unanswered");
    }

    private static Error NoData => new("Ping.NoData", "No data for this ping", ErrorType.NotFound);

    [Fact]
    public void AddScatterGather_ShouldWireTheHandlerAsNotificationHandler_WhenTheMediatorScansItsAssembly()
    {
        var services = AddScatterGather(mediatorFirst: true);

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(INotificationHandler<PingRequested>) &&
            descriptor.ImplementationType == typeof(ScatterGatherNotificationHandler<PingRequested, Ping, Pong>));
        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IScatterGatherHandler<PingRequested, Ping, Pong>) &&
            descriptor.ImplementationType == typeof(PingHandler));
    }

    [Fact]
    public void AddScatterGather_ShouldPutTheEventInTheScatterGatherLane_WhenTheMediatorScansItsAssembly()
        => LaneEvents(AddScatterGather(mediatorFirst: true))
            .ShouldBe([new ScatterGatherLaneEvent(typeof(PingRequested))]);

    [Fact]
    public void AddScatterGather_ShouldFindTheHandlers_WhenTheMediatorIsRegisteredAfterTheBus()
    {
        var services = AddScatterGather(mediatorFirst: false);

        ScatterGatherAdapters(services).ShouldBe(1);
        LaneEvents(services).ShouldBe([new ScatterGatherLaneEvent(typeof(PingRequested))]);
    }

    [Fact]
    public void AddScatterGather_ShouldRegisterEachHandlerOnce_WhenItsAssemblyIsScannedMoreThanOnce()
    {
        var assembly = typeof(ScatterGatherTests).Assembly;
        var services = new ServiceCollection();
        services.AddMediator(assembly);
        services.AddMessageBus(bus => bus.UseRabbitMq(Connection).AddScatterGather(assembly));
        services.AddMediator(assembly);

        ScatterGatherAdapters(services).ShouldBe(1);
        LaneEvents(services).Count.ShouldBe(1);
    }

    [Fact]
    public void AddScatterGather_ShouldFindNoHandlers_WhenNoAssemblyIsScanned()
    {
        var services = new ServiceCollection();
        services.AddMessageBus(bus => bus.UseRabbitMq(Connection).AddScatterGather());

        ScatterGatherAdapters(services).ShouldBe(0);
        LaneEvents(services).ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldHandTheGatheredRepliesToTheHandler_WhenThereAreRequests()
    {
        var handler = new PingHandler();
        var transport = new FakeScatterGatherTransport(key => new Pong("pong " + key));
        var adapter = new ScatterGatherNotificationHandler<PingRequested, Ping, Pong>(handler, transport);

        await adapter.Handle(new PingRequested(2), CancellationToken.None);

        transport.Sent.Keys.ShouldBe(["0", "1"], ignoreOrder: true);
        handler.Gathered!.Results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_ShouldGatherWithoutSending_WhenThereAreNoRequests()
    {
        var handler = new PingHandler();
        var transport = new FakeScatterGatherTransport(key => new Pong("pong " + key));
        var adapter = new ScatterGatherNotificationHandler<PingRequested, Ping, Pong>(handler, transport);

        await adapter.Handle(new PingRequested(0), CancellationToken.None);

        transport.Sent.ShouldBeEmpty();
        handler.Gathered!.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void CopyFrom_ShouldKeepOnlyTheCorrelationHeaders_WhenCopyingOntoAReply()
    {
        var batchId = Guid.NewGuid();
        var sent = new Dictionary<string, string>
        {
            [ScatterHeaders.BatchId] = batchId.ToString("D"),
            [ScatterHeaders.ItemKey] = "item-1",
            ["unrelated"] = "x",
        };

        var reply = ScatterHeaders.CopyFrom(sent);

        reply.Keys.ShouldBe([ScatterHeaders.BatchId, ScatterHeaders.ItemKey], ignoreOrder: true);
        ScatterHeaders.TryRead(reply, out var readBatchId, out var itemKey).ShouldBeTrue();
        readBatchId.ShouldBe(batchId);
        itemKey.ShouldBe("item-1");
    }

    [Fact]
    public void TryRead_ShouldReturnFalse_WhenMessageHasNoScatterHeaders()
        => ScatterHeaders.TryRead(new Dictionary<string, string>(), out _, out _).ShouldBeFalse();

    private static readonly RabbitMqConnection Connection = new("localhost", 5672, "guest", "guest");

    private static ServiceCollection AddScatterGather(bool mediatorFirst)
    {
        var services = new ServiceCollection();
        if (mediatorFirst)
            services.AddMediator(typeof(ScatterGatherTests).Assembly);
        services.AddMessageBus(bus => bus.UseRabbitMq(Connection).AddScatterGather());
        if (!mediatorFirst)
            services.AddMediator(typeof(ScatterGatherTests).Assembly);
        return services;
    }

    private static int ScatterGatherAdapters(ServiceCollection services)
        => services.Count(descriptor =>
            descriptor.ServiceType == typeof(INotificationHandler<PingRequested>) &&
            descriptor.ImplementationType == typeof(ScatterGatherNotificationHandler<PingRequested, Ping, Pong>));

    private static List<IOutboxLaneEvent> LaneEvents(ServiceCollection services)
        => [.. services
            .Where(descriptor => descriptor.ServiceType == typeof(IOutboxLaneEvent))
            .Select(descriptor => (IOutboxLaneEvent)descriptor.ImplementationInstance!)];

    internal sealed record Ping(string Value);

    internal sealed record Pong(string Value);

    internal sealed record PingRequested(int Items) : INotification;

    internal sealed class PingHandler : IScatterGatherHandler<PingRequested, Ping, Pong>
    {
        public IGathered<Pong>? Gathered { get; private set; }

        public TimeSpan Timeout => TimeSpan.FromSeconds(5);

        public Task<IReadOnlyDictionary<string, Ping>> ScatterAsync(PingRequested notification, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, Ping>>(
                Enumerable.Range(0, notification.Items).ToDictionary(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture), i => new Ping($"ping {i}")));

        public Task GatherAsync(PingRequested notification, IGathered<Pong> replies, CancellationToken cancellationToken)
        {
            Gathered = replies;
            return Task.CompletedTask;
        }
    }
}
