using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Diagnostics;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.UnitTests.PublishSubscribe;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.InMemory;

/// <summary>
/// The whole bus on the in-memory transport, without a broker: what a service gets locally and in its own tests
/// with <c>UseInMemory()</c>. Several hosts share one <see cref="InMemNetwork"/> to act as separate services.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class InMemoryBusTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RequestAsync_ShouldReturnTheResponse_WhenAResponderAnswers()
    {
        using var host = await StartServiceAsync(new InMemNetwork(), responder: new Responder());

        var reply = await host.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<Ping, Pong>(new Ping("hello"), Timeout);

        reply.IsSuccess.ShouldBeTrue();
        reply.Value.ShouldBe(new Pong("pong hello"));
    }

    [Fact]
    public async Task RequestAsync_ShouldReturnTheRespondersErrors_WhenItAnswersWithAFailure()
    {
        var noData = new Error("Ping.NoData", "Nothing to return", ErrorType.NotFound);
        using var host = await StartServiceAsync(new InMemNetwork(), responder: new Responder { Answer = _ => noData });

        var reply = await host.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<Ping, Pong>(new Ping("hello"), Timeout);

        reply.Errors.ShouldBe([noData]);
    }

    [Fact]
    public async Task RequestAsync_ShouldBeUnanswered_WhenNoResponderAnswersBeforeTheTimeout()
    {
        using var host = await StartServiceAsync(new InMemNetwork(), responder: null);

        var reply = await host.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<Ping, Pong>(new Ping("hello"), TimeSpan.FromMilliseconds(300));

        reply.Errors.Single().Type.ShouldBe(ErrorType.Timeout);
    }

    [Fact]
    public async Task RateLimitedQueue_ShouldCancelTheHandler_WhenItsRequesterStopsWaiting()
    {
        var responder = new Responder { Delay = TimeSpan.FromSeconds(30) };
        using var host = await StartServiceAsync(new InMemNetwork(), responder);

        var reply = await host.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<Ping, Pong>(new Ping("slow"), TimeSpan.FromMilliseconds(500));

        reply.Errors.Single().Type.ShouldBe(ErrorType.Timeout);
        (await responder.Cancelled.Task.WaitAsync(Timeout)).ShouldBeTrue();
    }

    [Fact]
    public async Task Publish_ShouldReachEveryService_WhenTwoServicesSubscribe()
    {
        var network = new InMemNetwork();
        var shardLog = new EventLog();
        var discordLog = new EventLog();
        using var shard = await StartServiceAsync(network, inputQueue: "shard", events: shardLog);
        using var discord = await StartServiceAsync(network, inputQueue: "discord", events: discordLog);
        using var publisher = await StartServiceAsync(network);

        await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new PlayerRenamedPublished("p1", "Han"));

        (await shardLog.WaitForAsync(1)).ShouldBe([new PlayerRenamed("p1", "Han")]);
        (await discordLog.WaitForAsync(1)).ShouldBe([new PlayerRenamed("p1", "Han")]);
    }

    [Fact]
    public async Task Publish_ShouldReachOneInstance_WhenAServiceRunsTwoInstances()
    {
        var network = new InMemNetwork();
        var log = new EventLog();
        using var first = await StartServiceAsync(network, inputQueue: "shard", events: log);
        using var second = await StartServiceAsync(network, inputQueue: "shard", events: log);
        using var publisher = await StartServiceAsync(network);
        var eventBus = publisher.Services.GetRequiredService<IEventBus>();

        for (var i = 0; i < 10; i++)
            await eventBus.PublishAsync(new PlayerRenamedPublished($"p{i}", "Han"));

        (await log.WaitForAsync(10)).Select(renamed => renamed.PlayerId).Distinct().Count().ShouldBe(10);
        await Task.Delay(200);
        log.Received.Count.ShouldBe(10);
    }

    [Fact]
    public async Task Metrics_ShouldCountRequestsAndEvents_WhenTheBusIsUsed()
    {
        var measurements = new ConcurrentBag<string>();
        using var listener = ListenTo(measurements);
        var network = new InMemNetwork();
        var log = new EventLog();
        using var host = await StartServiceAsync(network, inputQueue: "shard", events: log, responder: new Responder());
        using var publisher = await StartServiceAsync(network);

        await host.Services.GetRequiredService<IScatterGatherClient>().RequestAsync<Ping, Pong>(new Ping("hello"), Timeout);
        await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new PlayerRenamedPublished("p1", "Han"));
        await log.WaitForAsync(1);
        await Task.Delay(100);

        measurements.ShouldContain("messagebus.requests.handled outcome=success");
        measurements.ShouldContain("messagebus.scatter_gather.items outcome=answered");
        measurements.ShouldContain("messagebus.events.published");
        measurements.ShouldContain("messagebus.events.handled outcome=success");
    }

    private static MeterListener ListenTo(ConcurrentBag<string> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == MessageBusDiagnostics.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var outcome = string.Concat(tags.ToArray().Where(tag => tag.Key == "outcome").Select(tag => $" outcome={tag.Value}"));
            measurements.Add(instrument.Name + outcome);
        });
        listener.Start();
        return listener;
    }

    /// <summary>One service on the shared network: it asks Ping/Pong, answers them if it has a responder, and handles events if it has a log.</summary>
    private static async Task<IHost> StartServiceAsync(
        InMemNetwork network,
        Responder? responder = null,
        string? inputQueue = null,
        EventLog? events = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(events ?? new EventLog());
        builder.Services.AddSingleton(responder ?? new Responder());
        builder.Services.AddMessageBus(bus =>
        {
            bus.UseInMemory(network)
                .AddMessage<Ping>("test.ping.request.v1", sendTo: "test.ping")
                .AddMessage<Pong>("test.ping.response.v1")
                .AddScatterGather();
            if (inputQueue is not null)
                bus.WithInputQueue(inputQueue);
            if (events is not null)
                bus.AddMessage<PlayerRenamed>(PlayerRenamedWireName)
                    .AddMessage<IntegrationEventRegistrationTests.ShardRenamed>("test.shard-renamed.v1")
                    .AddEventHandlers(typeof(InMemoryBusTests).Assembly);
            else
                bus.AddMessage<PlayerRenamedPublished>(PlayerRenamedWireName);
            if (responder is not null)
                bus.AddRateLimitedQueue<Ping, Pong, PingHandler>("test.ping", queue => queue.PerSecond = 100);
        });
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private const string PlayerRenamedWireName = "test.player-renamed.v1";

    internal sealed record Ping(string Value);

    internal sealed record Pong(string Value);

    /// <summary>The publisher's class for the event.</summary>
    internal sealed record PlayerRenamedPublished(string PlayerId, string Name);

    /// <summary>The subscribers' class for the same event: bound by wire name, not by class.</summary>
    internal sealed record PlayerRenamed(string PlayerId, string Name);

    internal sealed class Responder
    {
        public Func<Ping, Result<Pong>> Answer { get; init; } = ping => new Pong("pong " + ping.Value);

        public TimeSpan Delay { get; init; } = TimeSpan.Zero;

        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class PingHandler(Responder responder) : IRequestResponder<Ping, Pong>
    {
        public async Task<Result<Pong>> HandleAsync(Ping request, CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(responder.Delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                responder.Cancelled.TrySetResult(true);
                throw;
            }
            return responder.Answer(request);
        }
    }

    internal sealed class EventLog
    {
        public ConcurrentQueue<PlayerRenamed> Received { get; } = new();

        public void Add(PlayerRenamed renamed)
            => Received.Enqueue(renamed);

        public async Task<IReadOnlyList<PlayerRenamed>> WaitForAsync(int count)
        {
            var giveUpAt = DateTime.UtcNow + Timeout;
            while (Received.Count < count)
            {
                if (DateTime.UtcNow > giveUpAt)
                    throw new TimeoutException($"Received {Received.Count} of {count} events.");
                await Task.Delay(20);
            }
            return [.. Received];
        }
    }

    internal sealed class PlayerRenamedHandler(EventLog log) : IIntegrationEventHandler<PlayerRenamed>
    {
        public Task<Result> HandleAsync(PlayerRenamed integrationEvent, CancellationToken cancellationToken)
        {
            log.Add(integrationEvent);
            return Task.FromResult(Result.Success());
        }
    }
}
