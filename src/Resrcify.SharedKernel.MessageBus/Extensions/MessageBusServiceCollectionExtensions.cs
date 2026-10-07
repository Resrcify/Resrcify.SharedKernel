using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.MessageBus.Broker;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rebus.Config;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.Retry;
using Rebus.Retry.Simple;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Diagnostics;
using Resrcify.SharedKernel.MessageBus.PublishSubscribe;
using Rebus.Routing.TypeBased;
using Rebus.Transport;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.Extensions;

/// <summary>Registers the service's message bus (Rebus on RabbitMQ).</summary>
public static class MessageBusServiceCollectionExtensions
{
    /// <summary>
    /// Adds the service's bus and its <see cref="IEventBus"/>. Defaults: System.Text.Json with web settings, explicit
    /// wire names only, prefetch 10, parallelism 20, Rebus' retries (5 attempts) then the service's own
    /// <c>&lt;input queue&gt;.error</c> queue, and tracing (Rebus.Diagnostics) on every bus. Destination queues of
    /// registered messages are declared when the bus starts, so a request sent before its responder's
    /// first start waits in its queue instead of being dropped. The builder's
    /// <see cref="Abstractions.IBusConfigurationStrategy"/> runs last on every bus, so it can change any default.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddMessageBus(bus => bus
    ///     .UseRabbitMq(new RabbitMqConnection(host, port, username, password))
    ///     .WithInputQueue("shardmanagement")
    ///     .AddMessage&lt;PlayerArenaProfileRequest&gt;("swgohapi.player-arena-profile.request.v1", sendTo: "swgohapi.player-arena-profile")
    ///     .AddMessage&lt;PlayerArenaProfileResponse&gt;("swgohapi.player-arena-profile.response.v1")
    ///     .AddScatterGather());
    /// </code>
    /// </example>
    public static IServiceCollection AddMessageBus(
        this IServiceCollection services,
        Action<MessageBusBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new MessageBusBuilder(services);
        configure(builder);
        var settings = builder.Settings;
        services.AddSingleton(settings);
        services.TryAddTransient<IEventBus, EventBus>();
        services.TryAddSingleton<ReceiveOrder>();
        services.TryAddSingleton<EventsInFlight>();
        // The clock deadlines, retries in place and health checks read; replace it (e.g. with FakeTimeProvider) in tests.
        services.TryAddSingleton(TimeProvider.System);
        if (!settings.IsInMemory)
        {
            services.AddSingleton(provider => new BrokerConnectionWatcher(
                settings,
                provider,
                provider.GetRequiredService<ILogger<BrokerConnectionWatcher>>()));
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<BrokerConnectionWatcher>());
        }

        services.AddRebus((rebus, provider) =>
        {
            var strategy = settings.ResolveConfigurationStrategy(provider);
            return rebus
                .Transport(transport => settings.ConfigureTransport(
                    transport,
                    provider,
                    settings.InputQueue,
                    strategy,
                    rabbitMq =>
                    {
                        rabbitMq.Prefetch(settings.Prefetch);
                        // Publish order holds across instances only if one instance consumes at a time; the others
                        // stand by and take over if it stops.
                        if (settings.HasPublishOrderedSubscriptions)
                            rabbitMq.InputQueueOptions(queue => queue.AddArgument("x-single-active-consumer", true));
                    }))
                .Serialization(serializer => settings.ConfigureSerialization(serializer, provider))
                .Routing(routing =>
                {
                    var typeBased = routing.TypeBased();
                    foreach (var (messageType, queue) in settings.Destinations)
                        typeBased.Map(messageType, queue);
                })
                .Options(options =>
                {
                    // A send-only (one-way) bus has no workers to configure.
                    if (settings.ErrorQueue is { } errorQueue)
                    {
                        options.SetNumberOfWorkers(1);
                        options.SetMaxParallelism(settings.MaxParallelism);
                        options.Decorate(context =>
                        {
                            var retry = WithErrorQueue(context.Get<RetryStrategySettings>(), errorQueue);
                            // Events are tried in place as often as the strategy would have delivered them.
                            settings.EventTries = Math.Max(1, retry.MaxDeliveryAttempts);
                            return retry;
                        });
                        options.Decorate<IErrorHandler>(context => new DeadLetterCountingErrorHandler(
                            context.Get<IErrorHandler>(),
                            settings.InputQueue!));
                        if (settings.HasPublishOrderedSubscriptions)
                        {
                            var order = provider.GetRequiredService<ReceiveOrder>();
                            options.Decorate<ITransport>(context => new ReceiveOrderTransport(context.Get<ITransport>(), order));
                            options.Decorate<IPipeline>(context => new PipelineStepInjector(context.Get<IPipeline>())
                                .OnReceive(
                                    new ReceiveOrderStep(order, settings),
                                    PipelineRelativePosition.After,
                                    typeof(DeserializeIncomingMessageStep)));
                        }
                    }
                    options.Decorate<ITransport>(context =>
                    {
                        var transport = context.Get<ITransport>();
                        settings.DeclareDestinations(transport, provider);
                        return transport;
                    });
                    settings.ConfigureEveryBus(options, provider);
                    strategy.ConfigureOptions(options);
                });
        });
        return services;
    }

    /// <summary>
    /// The service's own error queue instead of Rebus' shared <c>error</c>, unless the configuration strategy chose
    /// one; its other retry settings are kept.
    /// </summary>
    private static RetryStrategySettings WithErrorQueue(RetryStrategySettings retry, string errorQueue)
        => retry.ErrorQueueName != RetryStrategySettings.DefaultErrorQueueName
            ? retry
            : new RetryStrategySettings(
                errorQueue,
                retry.MaxDeliveryAttempts,
                retry.SecondLevelRetriesEnabled,
                retry.ErrorDetailsHeaderMaxLength,
                retry.ErrorTrackingMaxAgeMinutes,
                retry.ErrorQueueErrorCooldownTimeSeconds,
                retry.ErrorHandlerMode);
}
