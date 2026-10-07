using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Rebus.Config;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.Pipeline.Send;
using Rebus.Serialization;
using Rebus.Serialization.Json;
using Rebus.Time;
using Rebus.Transport;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Broker;
using Resrcify.SharedKernel.MessageBus.PublishSubscribe;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.MessageBus.Serialization;

namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>
/// Everything <see cref="MessageBusBuilder"/> collected, shared by the service's bus and every
/// rate-limited queue it hosts, so they all name and serialize messages the same way.
/// </summary>
internal sealed class MessageBusSettings
{
    private readonly Dictionary<Type, string> _explicitWireNames = new() { [typeof(ScatterRequestFailed)] = ScatterRequestFailed.WireName };
    private readonly HashSet<Type> _receivedTypes = [typeof(ScatterRequestFailed)];
    private readonly Dictionary<Type, string?> _destinations = [];
    private readonly ConcurrentDictionary<Type, object> _subscriptions = new();
    private readonly HashSet<string> _removedSubscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _publishersByEvent = new(StringComparer.Ordinal);

    // The (event, publisher) clashes already reported.
    private readonly ConcurrentDictionary<(string Event, string Publisher), byte> _reportedClashes = new();

    private JsonSerializerOptions? _jsonOptions;
    private IReadOnlyDictionary<string, Type>? _typesByWireName;

    public Func<IServiceProvider, RabbitMqConnection>? Connection { get; private set; }

    /// <summary>The in-memory network every bus of this service uses instead of RabbitMQ, when set.</summary>
    public InMemNetwork? InMemoryNetwork { get; private set; }

    public bool IsInMemory => InMemoryNetwork is not null;

    public Func<IServiceProvider, IBusConfigurationStrategy>? ConfigurationStrategy { get; set; }
    public Func<IServiceProvider, IMessageSerializationStrategy>? SerializationStrategy { get; set; }
    public string? InputQueue { get; set; }
    public int Prefetch { get; set; } = 10;
    public int MaxParallelism { get; set; } = 20;

    /// <summary>Message bodies of this many bytes or more are gzipped on every bus; <see langword="null"/>: never.</summary>
    public int? CompressAboveBytes { get; set; } = 32 * 1024;

    /// <summary>Each routed message type and the queue it is sent to (its own wire name unless given one).</summary>
    public IReadOnlyDictionary<Type, string> Destinations
        => _destinations.ToDictionary(entry => entry.Key, entry => entry.Value ?? WireNameOf(entry.Key));

    public void UseRabbitMq(Func<IServiceProvider, RabbitMqConnection> connection)
    {
        Connection = connection;
        InMemoryNetwork = null;
    }

    public void UseInMemory(InMemNetwork network)
    {
        InMemoryNetwork = network;
        Connection = null;
    }

    /// <summary>
    /// The name <paramref name="messageType"/> has on the wire: the one it was given, or else its class name (without
    /// the namespace, so each side can keep its own class under its own namespace).
    /// </summary>
    public string WireNameOf(Type messageType)
        => _explicitWireNames.TryGetValue(messageType, out var wireName) ? wireName : messageType.Name;

    /// <summary>Sends <paramref name="messageType"/> under <paramref name="wireName"/> instead of its class name.</summary>
    public void NameMessage(Type messageType, string wireName)
    {
        if (!_explicitWireNames.TryAdd(messageType, wireName) && _explicitWireNames[messageType] != wireName)
            throw new InvalidOperationException($"{messageType.Name} is already named '{_explicitWireNames[messageType]}'.");
    }

    /// <summary>This service can receive <paramref name="messageType"/>: a message arriving under its wire name becomes one.</summary>
    public void Receive(Type messageType)
        => _receivedTypes.Add(messageType);

    /// <summary>Sends <paramref name="messageType"/> to <paramref name="queue"/>, or to the queue named by its wire name.</summary>
    public void RouteTo(Type messageType, string? queue)
        => _destinations[messageType] = queue;

    /// <summary>
    /// The message types this service receives, by wire name: those registered, those its rate-limited queues and
    /// requests answer with, and the events it subscribes to. Built once, when the first bus reads a message.
    /// </summary>
    public IReadOnlyDictionary<string, Type> TypesByWireName(IServiceProvider provider)
    {
        if (_typesByWireName is not null)
            return _typesByWireName;

        var types = _explicitWireNames.Keys
            .Concat(_receivedTypes)
            .Concat(provider.GetServices<SubscribedEvent>().Select(subscribed => subscribed.EventType))
            .Distinct()
            .GroupBy(WireNameOf, StringComparer.Ordinal)
            .ToList();
        var clash = types.FirstOrDefault(group => group.Count() > 1);
        if (clash is not null)
            throw new InvalidOperationException(
                $"'{clash.Key}' names more than one message type ({string.Join(", ", clash.Select(type => type.FullName))}): " +
                "give one of them another name with AddMessage<T>(wireName).");

        var byWireName = types.ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        return Interlocked.CompareExchange(ref _typesByWireName, byWireName, null) ?? byWireName;
    }

    /// <summary>How the service handles <typeparamref name="TEvent"/>; the defaults unless configured.</summary>
    public SubscriptionOptions<TEvent> SubscriptionOptionsOf<TEvent>()
        where TEvent : class
        => (SubscriptionOptions<TEvent>)_subscriptions.GetOrAdd(typeof(TEvent), _ => new SubscriptionOptions<TEvent>());

    /// <summary>Whether the service handles <paramref name="eventType"/> in publish order.</summary>
    public bool HandlesInPublishOrder(Type eventType)
        => _subscriptions.TryGetValue(eventType, out var options)
            && options is IPublishOrderedSubscription { InPublishOrder: true };

    /// <summary>
    /// Whether any subscription is handled in publish order: the bus then numbers what it receives, and its RabbitMQ
    /// input queue has a single active consumer.
    /// </summary>
    public bool HasPublishOrderedSubscriptions
        => _subscriptions.Values.Any(options => options is IPublishOrderedSubscription { InPublishOrder: true });

    /// <summary>
    /// How many times an event's handler is tried (in place) before the event goes to the error queue: the bus' retry
    /// strategy's delivery attempts (5 unless a configuration strategy changes them), read when the bus starts.
    /// </summary>
    public int EventTries { get; set; } = 5;

    /// <summary>How long the service remembers the integration events it handled, to skip duplicates; null: it doesn't.</summary>
    public TimeSpan? RememberHandledEventsFor { get; set; }

    /// <summary>Events (by wire name) the service no longer handles: unsubscribed when it starts.</summary>
    public IReadOnlyCollection<string> RemovedSubscriptions => _removedSubscriptions;

    public void RemoveSubscription(string wireName)
        => _removedSubscriptions.Add(wireName);

    /// <summary>
    /// The name this service publishes under: the one it was given, else its input queue, else its entry assembly's
    /// name. Its instances share it.
    /// </summary>
    public string ServiceName
        => ServiceNameOverride ?? InputQueue ?? Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";

    /// <summary>The name given with <c>WithServiceName</c>, if any.</summary>
    public string? ServiceNameOverride { get; set; }

    /// <summary>
    /// Records that <paramref name="publisher"/> published <paramref name="wireName"/>: the service that published it
    /// first, if another one did (two services publishing events of the same name), else <see langword="null"/>. A pair
    /// is reported once: the clash is the same on every event, and a warning per event would flood the logs.
    /// </summary>
    public string? OtherPublisherOf(string wireName, string publisher)
    {
        var first = _publishersByEvent.GetOrAdd(wireName, publisher);
        if (string.Equals(first, publisher, StringComparison.Ordinal))
            return null;
        return _reportedClashes.TryAdd((wireName, publisher), 0) ? first : null;
    }

    /// <summary>The service's error queue: where its input queue's messages go after they kept failing.</summary>
    public string? ErrorQueue => InputQueue is null ? null : $"{InputQueue}.error";

    public RabbitMqConnection ResolveConnection(IServiceProvider provider)
        => Connection?.Invoke(provider)
            ?? throw new InvalidOperationException("The message bus needs a transport: call UseRabbitMq(...) or UseInMemory().");

    /// <summary>
    /// Points one bus at the service's transport: <paramref name="inputQueue"/> on RabbitMQ (send-only without
    /// one), with <paramref name="rabbitMq"/> and then the configuration strategy applied; or the same queue on
    /// the in-memory network, where neither applies.
    /// </summary>
    public void ConfigureTransport(
        StandardConfigurer<ITransport> transport,
        IServiceProvider provider,
        string? inputQueue,
        IBusConfigurationStrategy strategy,
        Action<RabbitMqOptionsBuilder>? rabbitMq = null)
    {
        if (InMemoryNetwork is { } network)
        {
            if (inputQueue is null)
                transport.UseInMemoryTransportAsOneWayClient(network);
            else
                transport.UseInMemoryTransport(network, inputQueue);
            return;
        }

        var connectionString = ResolveConnection(provider).ConnectionString;
        var options = inputQueue is null
            ? transport.UseRabbitMqAsOneWayClient(connectionString)
            : transport.UseRabbitMq(connectionString, inputQueue);
        rabbitMq?.Invoke(options);
        strategy.ConfigureTransport(options);
    }

    /// <summary>
    /// What every bus of the service has, whatever its job: tracing (Rebus.Diagnostics), unzipping of gzipped bodies,
    /// and gzip of bodies of <see cref="CompressAboveBytes"/> or more. The compression is wire-compatible with Rebus'
    /// <c>EnableCompression</c> both ways, at the fastest level (<see cref="MessageCompression"/>).
    /// </summary>
    public void ConfigureEveryBus(OptionsConfigurer options, IServiceProvider provider)
    {
        options.EnableDiagnosticSources();
        // Rebus' clock (a message's sent time, its expiry) is the container's, as every other clock here: with a
        // FakeTimeProvider a request's deadline is measured on one clock, not the sender's real one against a fake now.
        var time = provider.GetService<TimeProvider>() ?? TimeProvider.System;
        options.Register<IRebusTime>(_ => new TimeProviderRebusTime(time));
        options.Decorate<IPipeline>(context => WithCompression(context.Get<IPipeline>()));
    }

    private PipelineStepInjector WithCompression(IPipeline pipeline)
    {
        var injector = new PipelineStepInjector(pipeline)
            .OnReceive(
                DecompressIncomingMessageStep.Instance,
                PipelineRelativePosition.Before,
                typeof(DeserializeIncomingMessageStep));

        if (CompressAboveBytes is { } threshold)
            injector.OnSend(
                new CompressOutgoingMessageStep(threshold),
                PipelineRelativePosition.After,
                typeof(SerializeOutgoingMessageStep));

        return injector;
    }

    /// <summary>
    /// A connection factory for the connections the package makes itself (the broker watcher, the destination-queue
    /// check): the service's connection (its virtual host and TLS included), then the strategy's
    /// <c>ConfigureConnectionFactory</c>.
    /// </summary>
    public ConnectionFactory CreateConnectionFactory(IServiceProvider provider, string clientName)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(ResolveConnection(provider).ConnectionString),
            ClientProvidedName = clientName,
        };
        ResolveConfigurationStrategy(provider).ConfigureConnectionFactory(factory);
        return factory;
    }

    /// <summary>
    /// Declares the queues this bus sends to, so a message sent before the receiving service first ran isn't lost. A
    /// queue that exists already is left alone: its owner declared it with its own arguments (an ordered subscription's
    /// <c>x-single-active-consumer</c>), and declaring it again with others would fail this bus' start.
    /// </summary>
    public void DeclareDestinations(ITransport transport, IServiceProvider provider)
    {
        if (Destinations.Count == 0)
            return;
        if (IsInMemory)
        {
            foreach (var destination in Destinations.Values)
                transport.CreateQueue(destination);
            return;
        }

        var existing = DestinationQueues.Existing(this, provider, [.. Destinations.Values]);
        foreach (var destination in Destinations.Values.Where(destination => !existing.Contains(destination)))
            transport.CreateQueue(destination);
    }

    public IBusConfigurationStrategy ResolveConfigurationStrategy(IServiceProvider provider)
        => ConfigurationStrategy?.Invoke(provider) ?? DefaultBusConfigurationStrategy.Instance;

    /// <summary>
    /// The JSON options every bus shares, built once: a copy of the defaults, then the serialization
    /// strategy. Options are read-only once used, so they are never changed after this.
    /// </summary>
    public JsonSerializerOptions ResolveJsonOptions(IServiceProvider provider)
    {
        if (_jsonOptions is not null)
            return _jsonOptions;

        var options = new JsonSerializerOptions(MessageJson.Options);
        var strategy = SerializationStrategy?.Invoke(provider) ?? DefaultMessageSerializationStrategy.Instance;
        strategy.Configure(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return Interlocked.CompareExchange(ref _jsonOptions, options, null) ?? options;
    }

    /// <summary>
    /// System.Text.Json with web defaults plus the <see cref="IMessageSerializationStrategy"/>, and wire names
    /// (<see cref="WireNameConvention"/>): never a namespace or assembly on the wire.
    /// </summary>
    public void ConfigureSerialization(StandardConfigurer<ISerializer> serializer, IServiceProvider provider)
    {
        serializer.UseSystemTextJson(ResolveJsonOptions(provider));
        serializer
            .OtherService<IMessageTypeNameConvention>()
            .Register(_ => new WireNameConvention(this, provider));
    }
}
