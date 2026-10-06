using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rebus.Handlers;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>Finds the integration event handlers for <c>AddEventHandlers</c>.</summary>
internal static class IntegrationEventRegistration
{
    /// <summary>Registers the handlers in every assembly the mediator scans, whenever it scans them.</summary>
    internal static void FollowMediatorScansForEventHandlers(this IServiceCollection services)
        => services.FollowMediatorScans(MediatorScanListener.Instance);

    /// <summary>
    /// Every <see cref="IIntegrationEventHandler{TEvent}"/> in <paramref name="assemblies"/> is registered, Rebus gets
    /// a dispatcher for its event, and the event is subscribed to when the service starts. Scanning an assembly
    /// again adds nothing.
    /// </summary>
    internal static void RegisterIntegrationEventHandlers(this IServiceCollection services, IEnumerable<Assembly> assemblies)
    {
        var types = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false });

        foreach (var type in types)
            services.RegisterIntegrationEventHandler(type);
    }

    private static void RegisterIntegrationEventHandler(this IServiceCollection services, Type type)
    {
        foreach (var handlerInterface in type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>)))
        {
            services.TryAddEnumerable(ServiceDescriptor.Transient(handlerInterface, type));
            services.RegisterSubscription(handlerInterface.GetGenericArguments()[0]);
        }
    }

    /// <summary>Rebus gets a dispatcher for <paramref name="eventType"/>, and the event is subscribed to when the service starts.</summary>
    internal static void RegisterSubscription(this IServiceCollection services, Type eventType)
    {
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IHandleMessages<>).MakeGenericType(eventType),
            typeof(IntegrationEventDispatcher<>).MakeGenericType(eventType)));

        var subscribed = new SubscribedEvent(eventType);
        if (!services.Any(descriptor => subscribed.Equals(descriptor.ImplementationInstance)))
            services.AddSingleton(subscribed);
    }

    private sealed class MediatorScanListener : IMediatorAssemblyScanListener
    {
        public static MediatorScanListener Instance { get; } = new();

        public void OnScanned(IServiceCollection services, IReadOnlyCollection<Assembly> assemblies)
            => services.RegisterIntegrationEventHandlers(assemblies);
    }
}
