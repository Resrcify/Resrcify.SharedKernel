using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Caching.Primitives;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;

/// <summary>
/// One service on the bus for publish/subscribe: a publisher (send-only, its own class for the event), or a
/// subscriber with an input queue and an <see cref="IIntegrationEventHandler{TEvent}"/> (another class for the same
/// event). Each test uses its own wire name, so subscriptions left on the broker by earlier tests don't interfere.
/// </summary>
internal sealed class EventService : IAsyncDisposable
{
    private readonly IHost _host;

    private EventService(IHost host, EventLog log)
    {
        _host = host;
        Log = log;
    }

    public EventLog Log { get; }

    public static Task<EventService> StartPublisherAsync(
        RabbitMqConnection connection,
        string wireName,
        Action<MessageBusBuilder>? configure = null)
        => StartAsync(bus =>
        {
            bus.UseRabbitMq(connection)
                .AddMessage<PlayerRenamedPublished>(wireName);
            configure?.Invoke(bus);
        });

    public static Task<EventService> StartSubscriberAsync(
        RabbitMqConnection connection,
        string wireName,
        string inputQueue,
        Action<MessageBusBuilder>? configure = null,
        EventLog? log = null)
        => StartAsync(
            bus =>
            {
                bus.UseRabbitMq(connection)
                    .WithInputQueue(inputQueue)
                    .AddMessage<PlayerRenamed>(wireName)
                    .AddEventHandlers(typeof(EventService).Assembly);
                configure?.Invoke(bus);
            },
            log);

    /// <summary>A service that sends <see cref="PlayerRenamedPublished"/> straight to <paramref name="queue"/> (a command).</summary>
    public static Task<EventService> StartSenderAsync(RabbitMqConnection connection, string wireName, string queue)
        => StartAsync(bus => bus
            .UseRabbitMq(connection)
            .AddMessage<PlayerRenamedPublished>(wireName, sendTo: queue));

    public Task PublishAsync(PlayerRenamedPublished renamed)
        => _host.Services.GetRequiredService<IEventBus>().PublishAsync(renamed);

    public Task SendAsync(PlayerRenamedPublished renamed)
        => _host.Services.GetRequiredService<Rebus.Bus.IBus>().Send(renamed);

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private static async Task<EventService> StartAsync(Action<MessageBusBuilder> configure, EventLog? log = null)
    {
        log ??= new EventLog();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(log);
        builder.Services.AddDistributedMemoryCache().AddSingleton<ICachingService, DistributedCachingService>();
        builder.Services.AddMessageBus(configure);
        var host = builder.Build();
        await host.StartAsync();
        return new EventService(host, log);
    }
}

/// <summary>The publisher's class for the event.</summary>
internal sealed record PlayerRenamedPublished(string PlayerId, string Name, bool Fail = false);

/// <summary>The subscribers' class for the same event: bound by its wire name, not its class.</summary>
internal sealed record PlayerRenamed(string PlayerId, string Name, bool Fail);

internal sealed class EventLog
{
    private readonly ConcurrentDictionary<string, int> _runningByPlayer = new(StringComparer.Ordinal);
    private int _attempts;
    private int _running;

    public ConcurrentQueue<PlayerRenamed> Handled { get; } = new();

    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>How long handling one event takes, to see what runs at the same time.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>The most events handled at once, and the most for one player.</summary>
    public int MostAtOnce { get; private set; }

    public int MostAtOnceForOnePlayer { get; private set; }

    public void RecordAttempt()
        => Interlocked.Increment(ref _attempts);

    public async Task HandleAsync(PlayerRenamed renamed)
    {
        var running = Interlocked.Increment(ref _running);
        var forPlayer = _runningByPlayer.AddOrUpdate(renamed.PlayerId, 1, (_, count) => count + 1);
        lock (_runningByPlayer)
        {
            MostAtOnce = Math.Max(MostAtOnce, running);
            MostAtOnceForOnePlayer = Math.Max(MostAtOnceForOnePlayer, forPlayer);
        }
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay);
        _runningByPlayer.AddOrUpdate(renamed.PlayerId, 0, (_, count) => count - 1);
        Interlocked.Decrement(ref _running);
        Handled.Enqueue(renamed);
    }
}

/// <summary>Records every event; one marked <c>Fail</c> fails (a failure another try may fix) every time.</summary>
internal sealed class PlayerRenamedHandler(EventLog log) : IIntegrationEventHandler<PlayerRenamed>
{
    public async Task<Result> HandleAsync(PlayerRenamed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        log.RecordAttempt();
        if (integrationEvent.Fail)
            return Result.Failure(Error.ExternalFailure("Discord.Unavailable", "Always fails."));
        await log.HandleAsync(integrationEvent);
        return Result.Success();
    }
}
