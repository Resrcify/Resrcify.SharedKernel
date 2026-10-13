using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>Finds the scatter-gather handlers for <c>AddScatterGather</c>.</summary>
internal static class ScatterGatherRegistration
{
    /// <summary>Registers the handlers in every assembly the mediator scans, whenever it scans them.</summary>
    internal static void FollowMediatorScansForScatterGather(this IServiceCollection services)
        => services.FollowMediatorScans(MediatorScanListener.Instance);

    /// <summary>
    /// Every <see cref="IScatterGatherHandler{TEvent, TRequest, TResponse}"/> (and request scatter-gather handler) in <paramref name="assemblies"/>
    /// is wired as its event's notification handler (through
    /// <see cref="ScatterGatherNotificationHandler{TEvent, TRequest, TResponse}"/>), and its event is put in
    /// the outbox's <see cref="ScatterGatherLaneEvent.LaneName"/> lane. Scanning an assembly again adds nothing.
    /// </summary>
    internal static void RegisterScatterGatherHandlers(this IServiceCollection services, IEnumerable<Assembly> assemblies)
    {
        var types = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false });

        foreach (var type in types)
            services.RegisterScatterGatherHandler(type);
    }

    private static void RegisterScatterGatherHandler(this IServiceCollection services, Type type)
    {
        foreach (var handlerInterface in type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IScatterGatherHandler<,,>)))
        {
            var arguments = handlerInterface.GetGenericArguments();
            services.TryAddEnumerable(ServiceDescriptor.Transient(handlerInterface, type));
            services.TryAddEnumerable(ServiceDescriptor.Transient(
                typeof(INotificationHandler<>).MakeGenericType(arguments[0]),
                typeof(ScatterGatherNotificationHandler<,,>).MakeGenericType(arguments)));

            var laneEvent = new ScatterGatherLaneEvent(arguments[0]);
            if (!services.Any(descriptor => laneEvent.Equals(descriptor.ImplementationInstance)))
                services.AddSingleton<IOutboxLaneEvent>(laneEvent);
        }

        RegisterRequestHandler(services, type, typeof(IScatterGatherRequestHandler<,,,>), typeof(ScatterGatherRequestHandler<,,,>));
        RegisterRequestHandler(services, type, typeof(IStreamingScatterGatherRequestHandler<,,,>), typeof(StreamingScatterGatherRequestHandler<,,,>));
    }

    /// <summary>
    /// A request's scatter-gather handler is wired as the request's <c>IRequestHandler</c> (through
    /// <paramref name="adapter"/>), so it is sent like any other request.
    /// </summary>
    private static void RegisterRequestHandler(IServiceCollection services, Type type, Type handlerContract, Type adapter)
    {
        foreach (var handlerInterface in type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerContract))
        {
            var arguments = handlerInterface.GetGenericArguments();   // request, item request, item response, response
            services.TryAddEnumerable(ServiceDescriptor.Transient(handlerInterface, type));
            services.TryAddEnumerable(ServiceDescriptor.Transient(
                typeof(IRequestHandler<,>).MakeGenericType(arguments[0], arguments[3]),
                adapter.MakeGenericType(arguments)));
        }
    }

    private sealed class MediatorScanListener : IMediatorAssemblyScanListener
    {
        public static MediatorScanListener Instance { get; } = new();

        public void OnScanned(IServiceCollection services, IReadOnlyCollection<Assembly> assemblies)
            => services.RegisterScatterGatherHandlers(assemblies);
    }
}
