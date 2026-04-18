using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Messaging.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Resrcify.SharedKernel.Messaging.Runtime;
using Resrcify.SharedKernel.Messaging.Configuration;

namespace Resrcify.SharedKernel.Messaging.Extensions;

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
        MediatorConfigurationValidation.ValidateLifetime(mediatorLifetime, nameof(mediatorLifetime));

        services.RegisterMessagingTypes(assemblies);

        return services.AddMediatorRuntime(
            NotificationPublishStrategy.Sequential,
            useDiTimePipelineComposition: false,
            mediatorLifetime: mediatorLifetime);
    }

    public static IServiceCollection AddMediator(
        this IServiceCollection services,
        Action<MediatorConfiguration> configure)
    {
        var configuration = new MediatorConfiguration();
        configure(configuration);

        services.RegisterMessagingTypes(configuration.Assemblies);

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

        return services.AddMediatorRuntime(
            configuration.NotificationPublishStrategy,
            configuration.UseDiTimePipelineComposition,
            configuration.MediatorLifetime);
    }

    private static readonly HashSet<Type> SupportedOpenGenericTypes =
    [
        typeof(IRequestHandler<,>),
        typeof(IValueTaskRequestHandler<,>),
        typeof(IStreamRequestHandler<,>),
        typeof(INotificationHandler<>),
        typeof(IPipelineBehavior<,>),
        typeof(IValueTaskPipelineBehavior<,>),
        typeof(IRequestPipelineBehavior<,>),
        typeof(IValueTaskRequestPipelineBehavior<,>),
        typeof(IStreamPipelineBehavior<,>),
        typeof(IRequestPreProcessor<>),
        typeof(IRequestPostProcessor<,>)
    ];

    private static IServiceCollection RegisterMessagingTypes(
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

        return services;
    }

    private static IServiceCollection AddMediatorRuntime(
        this IServiceCollection services,
        NotificationPublishStrategy publishStrategy,
        bool useDiTimePipelineComposition,
        ServiceLifetime mediatorLifetime)
    {
        MediatorConfigurationValidation.ValidateLifetime(mediatorLifetime, nameof(mediatorLifetime));

        services.AddNotificationPublisher(publishStrategy);

        if (useDiTimePipelineComposition)
        {
            services.TryAddScoped(typeof(IDiComposedSendRuntime<,>), typeof(DiComposedSendRuntime<,>));
            services.TryAddScoped(typeof(IDiComposedStreamRuntime<,>), typeof(DiComposedStreamRuntime<,>));
        }

        services.TryAdd(ServiceDescriptor.Describe(typeof(IMediator), typeof(Mediator), mediatorLifetime));
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

        services.TryAddTransient(typeof(INotificationPublisher), implementationType);
    }
}
