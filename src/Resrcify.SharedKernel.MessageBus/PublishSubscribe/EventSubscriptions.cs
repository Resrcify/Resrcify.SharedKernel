using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>
/// Subscribes the service's input queue to every event it has a handler for, when the service starts. A
/// subscription is a binding on the broker that outlives the service, so subscribing again is harmless and a
/// removed handler's subscription stays until the binding is removed.
/// </summary>
/// <remarks>
/// The bus is resolved when the service starts, not when this is made: the host makes every hosted service before any
/// starts, and resolving <see cref="IBus"/> starts the bus, which would consume events before the migrations
/// (<c>AddMigrationsOnStartup</c>, at <c>StartingAsync</c>) had run.
/// </remarks>
internal sealed partial class EventSubscriptions(
    MessageBusSettings settings,
    IEnumerable<SubscribedEvent> subscribedEvents,
    IServiceProvider serviceProvider,
    ILogger<EventSubscriptions> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var eventTypes = subscribedEvents.Select(subscribed => subscribed.EventType).Distinct().ToList();
        if (eventTypes.Count == 0 && settings.RemovedSubscriptions.Count == 0)
            return;
        if (settings.InputQueue is null)
            throw new InvalidOperationException(
                "Handling integration events needs an input queue to receive them on: call WithInputQueue(\"<service name>\").");
        if (settings.RememberHandledEventsFor is not null && ClaimStores.Of(serviceProvider) is null)
            throw new InvalidOperationException(
                "SkipDuplicateEvents claims the events it handles in an IClaimStore: register one, or an ICachingService " +
                "that is one (e.g. AddDistributedMemoryCache() and AddSingleton<ICachingService, DistributedCachingService>()).");

        var bus = serviceProvider.GetRequiredService<IBus>();
        foreach (var topic in settings.RemovedSubscriptions)
        {
            await bus.Advanced.Topics.Unsubscribe(topic);
            LogUnsubscribed(settings.InputQueue, topic);
        }
        foreach (var eventType in eventTypes)
        {
            var topic = settings.WireNameOf(eventType);
            await bus.Advanced.Topics.Subscribe(topic);
            LogSubscribed(settings.InputQueue, topic);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "{Queue} subscribes to {Event}")]
    private partial void LogSubscribed(string queue, string @event);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Queue} unsubscribed from {Event}")]
    private partial void LogUnsubscribed(string queue, string @event);
}
