using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.MessageBus.PublishSubscribe;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Resrcify.SharedKernel.MessageBus.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>
/// Configures the service's message bus. Created by <c>AddMessageBus</c>; every setting has a working
/// default, so a service only states what is specific to it.
/// </summary>
public sealed class MessageBusBuilder
{
    internal MessageBusBuilder(IServiceCollection services)
        => Services = services;

    public IServiceCollection Services { get; }

    internal MessageBusSettings Settings { get; } = new();

    /// <summary>Connects to RabbitMQ, resolving the connection when the bus starts (e.g. from options).</summary>
    public MessageBusBuilder UseRabbitMq(Func<IServiceProvider, RabbitMqConnection> connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Settings.UseRabbitMq(connection);
        return this;
    }

    /// <inheritdoc cref="UseRabbitMq(Func{IServiceProvider, RabbitMqConnection})"/>
    public MessageBusBuilder UseRabbitMq(RabbitMqConnection connection)
        => UseRabbitMq(_ => connection);

    /// <summary>
    /// Runs every bus of this service in memory instead of on RabbitMQ: for local runs without a broker, and
    /// for tests. Everything works as on RabbitMQ (scatter-gather, rate-limited queues, publish/subscribe),
    /// within one process. Pass the same <paramref name="network"/> to several hosts in one test to let them
    /// talk to each other; without one, this service gets a network of its own.
    /// </summary>
    /// <remarks>Messages live only as long as the process, and the configuration strategy's transport settings are not applied.</remarks>
    public MessageBusBuilder UseInMemory(InMemNetwork? network = null)
    {
        Settings.UseInMemory(network ?? new InMemNetwork());
        return this;
    }

    /// <summary>
    /// Customises Rebus on every bus this service runs (its own, the scatter-gather reply bus and each
    /// rate-limited queue), after the defaults. The strategy is resolved when each bus starts.
    /// </summary>
    public MessageBusBuilder UseConfigurationStrategy(Func<IServiceProvider, IBusConfigurationStrategy> strategy)
    {
        Settings.ConfigurationStrategy = strategy;
        return this;
    }

    /// <inheritdoc cref="UseConfigurationStrategy(Func{IServiceProvider, IBusConfigurationStrategy})"/>
    public MessageBusBuilder UseConfigurationStrategy(IBusConfigurationStrategy strategy)
        => UseConfigurationStrategy(_ => strategy);

    /// <summary>
    /// Customises the JSON of every bus this service runs, e.g. to keep members off the wire. Resolved once,
    /// when the first bus starts.
    /// </summary>
    public MessageBusBuilder UseSerializationStrategy(Func<IServiceProvider, IMessageSerializationStrategy> strategy)
    {
        Settings.SerializationStrategy = strategy;
        return this;
    }

    /// <inheritdoc cref="UseSerializationStrategy(Func{IServiceProvider, IMessageSerializationStrategy})"/>
    public MessageBusBuilder UseSerializationStrategy(IMessageSerializationStrategy strategy)
        => UseSerializationStrategy(_ => strategy);

    /// <summary>
    /// The queue this service receives on: the integration events it subscribes to, and anything sent to it. Its
    /// instances share it. Messages that keep failing go to <c>&lt;queue&gt;.error</c>. Without one, the service's bus
    /// can only send and publish.
    /// </summary>
    public MessageBusBuilder WithInputQueue(string queueName)
    {
        Settings.InputQueue = queueName;
        return this;
    }

    /// <summary>
    /// The name this service publishes its events under (default: its input queue, else its entry assembly's name).
    /// Subscribers warn when two services publish events of the same name.
    /// </summary>
    public MessageBusBuilder WithServiceName(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        Settings.ServiceNameOverride = serviceName;
        return this;
    }

    /// <summary>
    /// Gzips message bodies of <paramref name="bytes"/> or more (default 32 KB) on every bus of the service, at the
    /// fastest level; <see langword="null"/> turns compression off. Every bus reads a gzipped message whatever its own
    /// setting, and the format is Rebus' own (<c>EnableCompression</c>), so services needn't agree on it.
    /// Rebus.Diagnostics' message size metric shows what is sent.
    /// </summary>
    public MessageBusBuilder CompressMessagesAbove(int? bytes)
    {
        if (bytes is not null)
            ArgumentOutOfRangeException.ThrowIfNegative(bytes.Value);
        Settings.CompressAboveBytes = bytes;
        return this;
    }

    /// <summary>Prefetch and parallelism for the input queue (defaults: 10 and 20).</summary>
    public MessageBusBuilder WithConcurrency(int prefetch, int maxParallelism)
    {
        Settings.Prefetch = prefetch;
        Settings.MaxParallelism = maxParallelism;
        return this;
    }

    /// <summary>
    /// Registers a message type this service receives, and optionally the name it goes by on the wire. Usually
    /// unnecessary: a message goes by its class name, and the types a service receives are known from its
    /// <see cref="AddRequest{TRequest, TResponse}"/>, <c>AddRateLimitedQueue</c> and <see cref="AddEventHandlers"/>.
    /// </summary>
    /// <param name="wireName">
    /// Only when this side's class has another name than the other side's, e.g.
    /// <c>AddMessage&lt;GuildPayload&gt;(nameof(GetGuildRequest))</c>, or for a new version of a message.
    /// </param>
    /// <param name="sendTo">The queue this type is sent to, for commands.</param>
    public MessageBusBuilder AddMessage<TMessage>(string? wireName = null, string? sendTo = null)
    {
        if (wireName is not null)
            Settings.NameMessage(typeof(TMessage), wireName);
        Settings.Receive(typeof(TMessage));
        if (sendTo is not null)
            Settings.RouteTo(typeof(TMessage), sendTo);
        return this;
    }

    /// <summary>
    /// Requester side: <typeparamref name="TRequest"/>s are sent to the provider's queue (named by the request's wire
    /// name, as the provider's <c>AddRateLimitedQueue</c> names it), and <typeparamref name="TResponse"/>s come back.
    /// Ask with <see cref="IScatterGatherClient"/> or an <see cref="IScatterGatherHandler{TEvent, TRequest, TResponse}"/>.
    /// </summary>
    /// <param name="queue">Only when the provider's queue has another name than the request's wire name.</param>
    public MessageBusBuilder AddRequest<TRequest, TResponse>(string? queue = null)
        where TRequest : class
        where TResponse : class
    {
        Settings.RouteTo(typeof(TRequest), queue);
        Settings.Receive(typeof(TResponse));
        return this;
    }

    /// <summary>
    /// Subscribes this service to every event it has an <see cref="IIntegrationEventHandler{TEvent}"/> for, found in
    /// the assemblies the mediator scans (whether <c>AddMediator</c> is called before or after this). It subscribes to
    /// each event by its wire name (its class name, unless <see cref="AddMessage{TMessage}"/> gave it another), on the
    /// service's input queue (<see cref="WithInputQueue"/>): its instances share it, so each event is handled once per
    /// service. Publishing (<see cref="IEventBus"/>) needs neither this nor an input queue.
    /// </summary>
    /// <param name="additionalAssemblies">Assemblies to scan for handlers besides the mediator's; usually none.</param>
    public MessageBusBuilder AddEventHandlers(params Assembly[] additionalAssemblies)
    {
        ArgumentNullException.ThrowIfNull(additionalAssemblies);

        Services.FollowMediatorScansForEventHandlers();
        Services.RegisterIntegrationEventHandlers(additionalAssemblies);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EventSubscriptions>());
        return this;
    }

    /// <summary>
    /// Subscribes to <typeparamref name="TEvent"/> and hands each one to the mediator as a notification, for the
    /// service's <c>INotificationHandler</c>s: an event handler that only does that needn't be written.
    /// </summary>
    /// <example><c>bus.ForwardEvent&lt;RankChangedEventMessage&gt;(message =&gt; new RankChangedEvent(message))</c></example>
    public MessageBusBuilder ForwardEvent<TEvent>(Func<TEvent, INotification> toNotification)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(toNotification);
        return AddForwarder<TEvent>(provider => new NotificationForwarder<TEvent>(
            provider.GetRequiredService<IPublisher>(),
            toNotification));
    }

    /// <summary>
    /// Subscribes to <typeparamref name="TEvent"/> and sends each one to the mediator as a command. The command's
    /// result is the event's: a failure that is the event's fault is logged and not retried, any other is retried in
    /// place and then moved to the error queue (see <see cref="IIntegrationEventHandler{TEvent}"/>).
    /// </summary>
    /// <example><c>bus.ForwardEvent&lt;ShardDeliveryDisabledEventMessage&gt;(message =&gt; new DisableDeliveryCommand(message.ShardId))</c></example>
    public MessageBusBuilder ForwardEvent<TEvent>(Func<TEvent, IRequest<Result>> toCommand)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(toCommand);
        return AddForwarder<TEvent>(provider => new CommandForwarder<TEvent>(
            provider.GetRequiredService<ISender>(),
            toCommand));
    }

    private MessageBusBuilder AddForwarder<TEvent>(Func<IServiceProvider, IIntegrationEventHandler<TEvent>> forwarder)
        where TEvent : class
    {
        Services.AddTransient(forwarder);
        Services.RegisterSubscription(typeof(TEvent));
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EventSubscriptions>());
        return this;
    }

    /// <summary>
    /// Handles each integration event at most once per service, even when it arrives again: a redelivery, or a
    /// publisher's outbox retrying and publishing it again (it keeps its message ID). Handled message IDs are kept in
    /// the service's <see cref="Abstractions.Caching.ICachingService"/> for <paramref name="rememberFor"/> (default: 3
    /// hours, well past an outbox retry or a redelivery, which come within minutes). Over an in-memory cache each
    /// instance remembers its own; over Redis, the whole service does.
    /// </summary>
    public MessageBusBuilder SkipDuplicateEvents(TimeSpan? rememberFor = null)
    {
        var remember = rememberFor ?? TimeSpan.FromHours(3);
        // A claim that expires at once, never, or after the cache keeps anything would dead-letter every event.
        if (remember <= TimeSpan.Zero || remember > ICachingService.MaxLifetime)
            throw new ArgumentOutOfRangeException(
                nameof(rememberFor),
                rememberFor,
                $"Handled events are remembered for a positive time no longer than {ICachingService.MaxLifetime.TotalDays} days.");
        Settings.RememberHandledEventsFor = remember;
        return this;
    }

    /// <summary>
    /// Unsubscribes from <typeparamref name="TEvent"/> when the service starts. A subscription is a binding on the
    /// broker that outlives the handler, so deleting a handler leaves its events arriving (and failing): keep this for
    /// one release after deleting it.
    /// </summary>
    public MessageBusBuilder RemoveSubscription<TEvent>()
        where TEvent : class
        => RemoveSubscription(Settings.WireNameOf(typeof(TEvent)));

    /// <inheritdoc cref="RemoveSubscription{TEvent}"/>
    /// <param name="wireName">The event's wire name (its class name), once the class itself is gone.</param>
    public MessageBusBuilder RemoveSubscription(string wireName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireName);
        Settings.RemoveSubscription(wireName);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EventSubscriptions>());
        return this;
    }

    /// <summary>
    /// How this service handles <typeparamref name="TEvent"/>, e.g. one at a time per key:
    /// <c>ConfigureSubscription&lt;PayoutRotated&gt;(options =&gt; options.HandleInPartitions(e =&gt; e.ShardId))</c>.
    /// </summary>
    public MessageBusBuilder ConfigureSubscription<TEvent>(Action<SubscriptionOptions<TEvent>> configure)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Settings.SubscriptionOptionsOf<TEvent>());
        return this;
    }

    /// <summary>Registers a handler for messages arriving on the input queue.</summary>
    public MessageBusBuilder AddHandler<THandler>()
        where THandler : IHandleMessages
    {
        Services.AddRebusHandler<THandler>();
        return this;
    }

    /// <summary>
    /// Requester side of scatter-gather: lets <see cref="IScatterGatherHandler{TEvent, TRequest, TResponse}"/>s
    /// send their requests and wait for the replies, on a reply queue private to this instance. The
    /// handlers are found in the assemblies the mediator scans, whether <c>AddMediator</c> is called before
    /// or after this, and their events are put in the outbox's scatter-gather lane. Needs the mediator and,
    /// for durability, the outbox (<c>AddOutboxProcessing</c>, or <c>AddOutboxLanes</c> next to a directly
    /// registered outbox job).
    /// </summary>
    /// <param name="additionalAssemblies">Assemblies to scan for handlers besides the mediator's; usually none.</param>
    public MessageBusBuilder AddScatterGather(params Assembly[] additionalAssemblies)
    {
        ArgumentNullException.ThrowIfNull(additionalAssemblies);

        Services.FollowMediatorScansForScatterGather();
        Services.RegisterScatterGatherHandlers(additionalAssemblies);
        Services.AddSingleton(provider => new ScatterGatherTransport(
            Settings,
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider,
            provider.GetRequiredService<ILogger<ScatterGatherTransport>>()));
        Services.AddSingleton<IScatterGatherClient>(provider => provider.GetRequiredService<ScatterGatherTransport>());
        // Before every hosted service registered so far: the host starts them in order and stops them in reverse, so
        // the reply bus is up before the outbox lanes gather (even when AddOutboxProcessing came first) and still up
        // while they finish their gathers at shutdown.
        Services.Insert(
            FirstHostedServiceIndex(Services),
            ServiceDescriptor.Singleton<IHostedService>(provider => provider.GetRequiredService<ScatterGatherTransport>()));
        return this;
    }

    /// <summary>
    /// Responder side: consumes the queue named by <typeparamref name="TRequest"/>'s wire name (which is where
    /// requesters' <see cref="AddRequest{TRequest, TResponse}"/> sends it) on a bus of its own, under its own limit per
    /// instance, with <typeparamref name="THandler"/> producing each reply.
    /// </summary>
    public MessageBusBuilder AddRateLimitedQueue<TRequest, TResponse, THandler>(
        Action<RateLimitedQueueOptions>? configure = null)
        where TRequest : class
        where TResponse : class
        where THandler : class, IRequestResponder<TRequest, TResponse>
        => AddRateLimitedQueue<TRequest, TResponse, THandler>(queueName: null, configure);

    /// <summary>
    /// Responder side, on a queue named <paramref name="queueName"/> instead of by the request's wire name.
    /// </summary>
    public MessageBusBuilder AddRateLimitedQueue<TRequest, TResponse, THandler>(
        string? queueName,
        Action<RateLimitedQueueOptions>? configure = null)
        where TRequest : class
        where TResponse : class
        where THandler : class, IRequestResponder<TRequest, TResponse>
    {
        var options = new RateLimitedQueueOptions();
        configure?.Invoke(options);
        Services.AddScoped<IRequestResponder<TRequest, TResponse>, THandler>();
        Services.TryAddScoped<THandler>();
        // This queue's own handler: two queues for the same request type each answer with theirs.
        return AddQueueHost<TRequest, TResponse>(queueName, options, provider => provider.GetRequiredService<THandler>());
    }

    /// <summary>
    /// Responder side, answered through the mediator: each request becomes the mediator request
    /// <paramref name="toMediatorRequest"/> makes, and its handler answers, so the queue needs no
    /// <see cref="IRequestResponder{TRequest, TResponse}"/> of its own. A failure is answered or retried as
    /// <see cref="RateLimitedQueueOptions.AnswerFailure"/> says. The queue is named by <typeparamref name="TRequest"/>'s
    /// wire name.
    /// </summary>
    /// <example><c>bus.AddRateLimitedQueue&lt;GetGuildRequest, GetGuildResponse&gt;(request =&gt; new GetGuild(request))</c></example>
    public MessageBusBuilder AddRateLimitedQueue<TRequest, TResponse>(
        Func<TRequest, IRequest<Result<TResponse>>> toMediatorRequest,
        Action<RateLimitedQueueOptions>? configure = null)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(toMediatorRequest);
        var options = new RateLimitedQueueOptions();
        configure?.Invoke(options);
        return AddQueueHost<TRequest, TResponse>(
            queueName: null,
            options,
            provider => new MediatorRequestResponder<TRequest, TResponse>(provider.GetRequiredService<ISender>(), toMediatorRequest));
    }

    // One host per queue, keyed by its name: two queues for the same request and response types (a priority queue
    // next to the regular one) each get their own bus, limit and responder, and both are started.
    private MessageBusBuilder AddQueueHost<TRequest, TResponse>(
        string? queueName,
        RateLimitedQueueOptions options,
        Func<IServiceProvider, IRequestResponder<TRequest, TResponse>> responderFactory)
        where TRequest : class
        where TResponse : class
    {
        var name = queueName ?? Settings.WireNameOf(typeof(TRequest));
        if (Services.Any(descriptor => descriptor.ImplementationInstance is RateLimitedQueueName registered && registered.Name == name))
            throw new InvalidOperationException($"The rate-limited queue {name} is registered twice: give each queue its own name.");

        Settings.Receive(typeof(TRequest));
        Services.TryAddSingleton<HealthGates>();
        Services.AddSingleton(new RateLimitedQueueName(name));
        Services.AddKeyedSingleton(name, (provider, _) => new RateLimitedQueueHost<TRequest, TResponse>(
            name,
            options,
            Settings,
            provider,
            responderFactory,
            provider.GetRequiredService<ILogger<RateLimitedQueueHost<TRequest, TResponse>>>()));
        Services.AddSingleton<IHostedService>(provider => provider.GetRequiredKeyedService<RateLimitedQueueHost<TRequest, TResponse>>(name));
        Services.AddSingleton<IQueueConsumer>(provider => provider.GetRequiredKeyedService<RateLimitedQueueHost<TRequest, TResponse>>(name));
        return this;
    }

    /// <summary>
    /// The index of the first hosted service registered so far (the end when none is): what is inserted there starts
    /// before them all and stops after them.
    /// </summary>
    private static int FirstHostedServiceIndex(IServiceCollection services)
    {
        for (var index = 0; index < services.Count; index++)
        {
            if (services[index].ServiceType == typeof(IHostedService))
                return index;
        }

        return services.Count;
    }
}
