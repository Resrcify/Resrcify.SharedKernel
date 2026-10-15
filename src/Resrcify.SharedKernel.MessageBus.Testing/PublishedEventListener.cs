using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Pipeline;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.PublishSubscribe;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.Testing;

/// <summary>
/// Listens on a real broker for the integration events a service publishes, as another service subscribed to them
/// would: a bus of its own on RabbitMQ, with an input queue of its own (<see cref="InputQueue"/>) bound to each event's
/// topic, recording what arrives in <see cref="Received"/>. Start it before the publish (once
/// <see cref="StartAsync"/> returns, its queue is bound to every event it listens to: a publish before that has nowhere
/// to go); dispose it to unsubscribe and stop.
/// </summary>
/// <example>
/// <code>
/// await using var events = await PublishedEventListener.StartAsync(
///     fixture.RabbitMq!.Connection,
///     listen =&gt; listen.Listen&lt;ShardCreated&gt;());
/// await client.PostAsJsonAsync("/shards", new CreateShardRequest("Main"));
/// var created = await events.Received.WaitForAsync&lt;ShardCreated&gt;(e =&gt; e.Name == "Main");
/// </code>
/// </example>
public sealed class PublishedEventListener
    : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly IReadOnlyList<string> _topics;
    private bool _disposed;

    private PublishedEventListener(
        IHost host,
        string inputQueue,
        IReadOnlyList<string> topics,
        MessageRecording received)
    {
        _host = host;
        InputQueue = inputQueue;
        _topics = topics;
        Received = received;
    }

    /// <summary>The listener's own queue, bound to the topic of each event it listens to.</summary>
    public string InputQueue { get; }

    /// <summary>The events that arrived, in order, with their headers (the publisher's message ID among them).</summary>
    public MessageRecording Received { get; }

    /// <summary>
    /// Starts listening on <paramref name="broker"/> (the publisher's broker and virtual host) to the events
    /// <paramref name="listen"/> names. Returns once the listener's queue is bound to each of them.
    /// </summary>
    /// <param name="broker">The broker, as the test process reaches it (e.g. <c>RabbitMqContainerFixture.Connection</c>).</param>
    /// <param name="listen">The events to listen to: <c>listen =&gt; listen.Listen&lt;ShardCreated&gt;()</c>.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <exception cref="ArgumentException"><paramref name="listen"/> names no event.</exception>
    public static async Task<PublishedEventListener> StartAsync(
        RabbitMqConnection broker,
        Action<PublishedEventListenerBuilder> listen,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(listen);
        var events = new PublishedEventListenerBuilder();
        listen(events);
        if (events.Registrations.Count == 0)
            throw new ArgumentException("Name at least one event to listen to: listen => listen.Listen<TEvent>().", nameof(listen));

        var inputQueue = $"published-events-{Guid.NewGuid():N}";
        var received = new MessageRecording("received");
        var topics = new List<string>();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new ListenerRecording(received, inputQueue));
        builder.Services.AddMessageBus(bus =>
        {
            bus.UseRabbitMq(broker).WithInputQueue(inputQueue);
            foreach (var register in events.Registrations)
                topics.Add(register(bus));
        });

        var host = builder.Build();
        try
        {
            await host.StartAsync(cancellationToken);
        }
        catch
        {
            host.Dispose();
            throw;
        }

        return new PublishedEventListener(host, inputQueue, topics, received);
    }

    /// <summary>Unsubscribes the listener's queue from every topic (so it stops collecting events), then stops its bus.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            var bus = _host.Services.GetRequiredService<IBus>();
            foreach (var topic in _topics)
                await bus.Advanced.Topics.Unsubscribe(topic);
            await _host.StopAsync();
        }
        finally
        {
            _host.Dispose();
        }
    }
}

/// <summary>The events a <see cref="PublishedEventListener"/> listens to.</summary>
public sealed class PublishedEventListenerBuilder
{
    internal List<Func<MessageBusBuilder, string>> Registrations { get; } = [];

    internal PublishedEventListenerBuilder()
    {
    }

    /// <summary>
    /// Listens to <typeparamref name="TEvent"/>, by its wire name: its class name, or <paramref name="wireName"/> when
    /// the publisher gives it another (<c>AddMessage&lt;T&gt;(wireName)</c>).
    /// </summary>
    public PublishedEventListenerBuilder Listen<TEvent>(string? wireName = null)
        where TEvent : class
    {
        Registrations.Add(bus =>
        {
            bus.AddMessage<TEvent>(wireName);
            bus.Services.AddTransient<IIntegrationEventHandler<TEvent>>(provider =>
                new RecordingEventHandler<TEvent>(provider.GetRequiredService<ListenerRecording>()));
            bus.Services.RegisterSubscription(typeof(TEvent));
            bus.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EventSubscriptions>());
            return bus.Settings.WireNameOf(typeof(TEvent));
        });
        return this;
    }
}

/// <summary>Where a listener's events go, and the queue they came from.</summary>
internal sealed record ListenerRecording(
    MessageRecording Received,
    string Queue);

/// <summary>Records each <typeparamref name="TEvent"/> the listener receives, with its headers.</summary>
internal sealed class RecordingEventHandler<TEvent>(ListenerRecording recording)
    : IIntegrationEventHandler<TEvent>
    where TEvent : class
{
    public Task<Result> HandleAsync(
        TEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        recording.Received.Add(MessageContext.Current?.Message is { } message
            ? RecordedMessage.Received(message, recording.Queue)
            : new RecordedMessage(integrationEvent, new Dictionary<string, string>(StringComparer.Ordinal), [], recording.Queue, null));
        return Task.FromResult(Result.Success());
    }
}
