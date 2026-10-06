using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Resrcify.SharedKernel.Mediator.Runtime;
using Resrcify.SharedKernel.Mediator.Configuration;
using Resrcify.SharedKernel.Mediator.Abstractions;

namespace Resrcify.SharedKernel.Mediator.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediator(
        this IServiceCollection services,
        params Assembly[] assemblies)
        => services.AddMediator(ServiceLifetime.Transient, assemblies);

    public static IServiceCollection AddMediator(
        this IServiceCollection services,
        ServiceLifetime mediatorLifetime,
        params Assembly[] assemblies)
    {
        MediatorConfigurationValidation.ValidateMediatorLifetime(mediatorLifetime, nameof(mediatorLifetime));

        services.RegisterMediatorTypes(assemblies);
        BehaviorCheck.Run(services, assemblies, configuredBehaviorTypes: []);

        return services.AddMediatorRuntime(
            NotificationPublishStrategy.Sequential,
            useDiTimePipelineComposition: false,
            mediatorLifetime: mediatorLifetime);
    }

    public static IServiceCollection AddMediator(
        this IServiceCollection services,
        Action<MediatorConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var configuration = new MediatorConfiguration();
        configure(configuration);

        services.RegisterMediatorTypes(configuration.Assemblies);
        if (configuration.ChecksBehaviors)
            BehaviorCheck.Run(services, configuration.Assemblies, configuration.OpenBehaviorTypes);

        foreach (var behaviorRegistration in configuration.OpenBehaviorRegistrations)
        {
            foreach (var serviceType in OpenBehaviorServiceCollectionExtensions.GetOpenBehaviorServiceTypes(behaviorRegistration.BehaviorType))
            {
                services.RemoveMatchingOpenBehaviorRegistration(serviceType, behaviorRegistration.BehaviorType);

                services.TryAddEnumerable(
                    ServiceDescriptor.Describe(
                        serviceType,
                        behaviorRegistration.BehaviorType,
                        behaviorRegistration.Lifetime));
            }
        }

        foreach (var processorRegistration in configuration.ProcessorRegistrations)
            services.TryAddEnumerable(processorRegistration);

        // How the logging behavior logs: the configured options (the behavior's defaults when not configured).
        services.TryAddSingleton(configuration.LoggingOptions);
        // How the unit-of-work behavior saves: throwing (the default) or returning persistence failures as results.
        services.TryAddSingleton(configuration.UnitOfWorkOptions);

        return services.AddMediatorRuntime(
            configuration.NotificationPublishStrategy,
            configuration.UseDiTimePipelineComposition,
            configuration.MediatorLifetime);
    }

    // Handlers only. Behaviors and processors run only when registered explicitly (AddOpenBehavior,
    // AddStandardBehaviors, AddRequestPreProcessor/AddRequestPostProcessor, or the container), in the order registered:
    // registered by the scan, they ran whether asked for or not, outermost, in reflection order.
    private static readonly HashSet<Type> SupportedOpenGenericTypes =
    [
        typeof(IRequestHandler<,>),
        typeof(IValueTaskRequestHandler<,>),
        typeof(IStreamRequestHandler<,>),
        typeof(INotificationHandler<>)
    ];

    private static IServiceCollection RegisterMediatorTypes(
        this IServiceCollection services,
        IReadOnlyCollection<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var types = assembly
                .GetTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false });

            foreach (var type in types)
            {
                var implementedInterfaces = type.GetInterfaces()
                    .Where(i => i.IsGenericType)
                    .Where(i => SupportedOpenGenericTypes.Contains(i.GetGenericTypeDefinition()));

                foreach (var implementedInterface in implementedInterfaces)
                {
                    if (implementedInterface.ContainsGenericParameters || type.ContainsGenericParameters)
                    {
                        var serviceType = implementedInterface.IsGenericTypeDefinition
                            ? implementedInterface
                            : implementedInterface.GetGenericTypeDefinition();

                        var implementationType = type.IsGenericTypeDefinition
                            ? type
                            : type.GetGenericTypeDefinition();

                        if (!serviceType.IsGenericTypeDefinition || !implementationType.IsGenericTypeDefinition)
                            continue;

                        if (serviceType.GetGenericArguments().Length != implementationType.GetGenericArguments().Length)
                            continue;

                        services.TryAddEnumerable(
                            ServiceDescriptor.Transient(
                                serviceType,
                                implementationType));
                        continue;
                    }

                    services.TryAddEnumerable(
                        ServiceDescriptor.Transient(implementedInterface, type));
                }
            }
        }

        services.AnnounceAssemblyScan(assemblies);
        return services;
    }

    /// <summary>
    /// Records the scanned assemblies and tells the listeners registered so far, so packages that look for
    /// handlers of their own (the message bus) follow this scan whichever is registered first.
    /// </summary>
    private static void AnnounceAssemblyScan(
        this IServiceCollection services,
        IReadOnlyCollection<Assembly> assemblies)
    {
        var scan = new MediatorAssemblyScan([.. assemblies]);
        services.AddSingleton<IMediatorAssemblyScan>(scan);

        var listeners = services
            .Where(descriptor => descriptor.ServiceType == typeof(IMediatorAssemblyScanListener))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IMediatorAssemblyScanListener>()
            .ToList();
        foreach (var listener in listeners)
            listener.OnScanned(services, scan.Assemblies);
    }

    private sealed class MediatorAssemblyScan(IReadOnlyCollection<Assembly> assemblies)
        : IMediatorAssemblyScan
    {
        public IReadOnlyCollection<Assembly> Assemblies { get; } = assemblies;
    }

    private static IServiceCollection AddMediatorRuntime(
        this IServiceCollection services,
        NotificationPublishStrategy publishStrategy,
        bool useDiTimePipelineComposition,
        ServiceLifetime mediatorLifetime)
    {
        MediatorConfigurationValidation.ValidateMediatorLifetime(mediatorLifetime, nameof(mediatorLifetime));

        services.AddNotificationPublisher(publishStrategy);
        // The clock the logging behavior reads; replace it (e.g. with FakeTimeProvider) in tests.
        services.TryAddSingleton(TimeProvider.System);

        // Transient: each mediator keeps the runtime it built (one slot per kind of call), so scoping it only cost
        // every scope its tracking dictionary.
        if (useDiTimePipelineComposition)
        {
            services.TryAddTransient(typeof(IDiComposedSendRuntime<,>), typeof(DiComposedSendRuntime<,>));
            services.TryAddTransient(typeof(IDiComposedStreamRuntime<,>), typeof(DiComposedStreamRuntime<,>));
        }

        services.TryAdd(ServiceDescriptor.Describe(typeof(IMediator), typeof(Runtime.Mediator), mediatorLifetime));
        if (mediatorLifetime == ServiceLifetime.Transient)
        {
            // Transient: every resolution is a new mediator anyway, so build each interface directly rather than
            // through a factory that resolves IMediator (a second lookup on every resolution).
            services.TryAdd(ServiceDescriptor.Transient<ISender, Runtime.Mediator>());
            services.TryAdd(ServiceDescriptor.Transient<IPublisher, Runtime.Mediator>());
            services.TryAdd(ServiceDescriptor.Transient<IStreamSender, Runtime.Mediator>());
            return services;
        }

        // Scoped: every interface is the scope's one mediator.
        services.TryAdd(ServiceDescriptor.Describe(typeof(ISender), sp => sp.GetRequiredService<IMediator>(), mediatorLifetime));
        services.TryAdd(ServiceDescriptor.Describe(typeof(IPublisher), sp => sp.GetRequiredService<IMediator>(), mediatorLifetime));
        services.TryAdd(ServiceDescriptor.Describe(typeof(IStreamSender), sp => sp.GetRequiredService<IMediator>(), mediatorLifetime));

        return services;
    }

    private static void AddNotificationPublisher(
        this IServiceCollection services,
        NotificationPublishStrategy publishStrategy)
    {
        var implementationType = publishStrategy switch
        {
            NotificationPublishStrategy.Sequential => typeof(ForeachAwaitNotificationPublisher),
            NotificationPublishStrategy.Parallel => typeof(TaskWhenAllNotificationPublisher),
            _ => typeof(ForeachAwaitNotificationPublisher)
        };

        // Both publishers are stateless: one instance, not one per mediator.
        services.TryAddSingleton(typeof(INotificationPublisher), implementationType);
    }
}
