using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentValidation;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Resrcify.SharedKernel.Mediator.Runtime;
using Resrcify.SharedKernel.Mediator.Configuration;
using Resrcify.SharedKernel.Mediator.Abstractions;

namespace Resrcify.SharedKernel.Mediator.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mediator: the handlers of the assemblies <paramref name="configure"/> names
    /// (<see cref="MediatorConfiguration.RegisterServicesFromAssemblies"/>), their FluentValidation validators (unless
    /// <see cref="MediatorConfiguration.SkipValidatorScan"/>), and the behaviors and processors it adds, in order. Each request's pipeline (its handler, behaviors and processors) is
    /// resolved from the container as one service, so the container builds it with their lifetimes: transient unless
    /// registered otherwise, the mediator too (<see cref="MediatorConfiguration.UseMediatorLifetime"/>).
    /// </summary>
    public static IServiceCollection AddMediator(
        this IServiceCollection services,
        Action<MediatorConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var configuration = new MediatorConfiguration();
        configure(configuration);

        services.RegisterMediatorTypes(configuration.Assemblies);
        if (configuration.ScansValidators)
            services.RegisterValidators(configuration.Assemblies);
        if (configuration.ChecksBehaviors)
            BehaviorCheck.Run(
                services,
                configuration.Assemblies,
                configuration.OpenBehaviorTypes);

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

        // How the logging behavior logs, and how the unit-of-work behavior saves (throwing, the default, or returning
        // persistence failures as results): the options configured in any AddMediator call, defaults otherwise.
        RegisterOptions(services, configuration.LoggingOptions, configuration.LoggingConfigured, nameof(MediatorConfiguration.ConfigureLogging));
        RegisterOptions(services, configuration.UnitOfWorkOptions, configuration.UnitOfWorkConfigured, nameof(MediatorConfiguration.ConfigureUnitOfWork));
        RegisterOptions(services, configuration.IdempotencyOptions, configuration.IdempotencyConfigured, nameof(MediatorConfiguration.ConfigureIdempotency));
        // Which requests of a scope were answered with an earlier result: the HTTP endpoints read it.
        services.TryAddScoped<IdempotencyContext>();
        services.TryAddScoped<IIdempotencyContext>(provider => provider.GetRequiredService<IdempotencyContext>());
        // The locks of the non-concurrent requests: one set per container, so every scope's requests share them.
        services.TryAddSingleton<NonConcurrentLocks>();

        return services.AddMediatorRuntime(
            configuration.NotificationPublishStrategy,
            configuration.MediatorLifetime);
    }

    // The options instances an AddMediator call configured (not just defaulted), so a later call can tell them apart.
    private static readonly ConditionalWeakTable<object, object> ConfiguredOptions = [];

    // Several AddMediator calls (e.g. one per layer) share one options instance per kind: the one a call configured wins
    // over another's defaults, whichever comes first. Two calls configuring it can't both win: that throws, rather than
    // silently dropping one (a ReturnPersistenceFailures set and then lost turns a 409 into a 500).
    private static void RegisterOptions<TOptions>(
        IServiceCollection services,
        TOptions options,
        bool configured,
        string configureMethod)
        where TOptions : class
    {
        var existing = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(TOptions));
        if (existing is null)
        {
            services.AddSingleton(options);
        }
        else if (configured)
        {
            if (existing.ImplementationInstance is { } earlier
                && !ReferenceEquals(earlier, options)
                && ConfiguredOptions.TryGetValue(earlier, out _))
                throw new InvalidOperationException(
                    $"Two AddMediator calls both call cfg.{configureMethod}: configure {typeof(TOptions).Name} in one " +
                    "of them only.");
            services.Remove(existing);
            services.AddSingleton(options);
        }

        if (configured)
            ConfiguredOptions.AddOrUpdate(options, options);
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
    /// Registers every FluentValidation validator of <paramref name="assemblies"/>, internal ones included, as
    /// <c>AddValidatorsFromAssembly</c> does (scoped, as its <c>IValidator&lt;T&gt;</c> and as itself), and with its
    /// semantics: a validator registered already (by <c>AddValidatorsFromAssembly</c>, before or after, or by hand) isn't
    /// registered again, so the validation behavior never runs one twice.
    /// </summary>
    private static void RegisterValidators(
        this IServiceCollection services,
        IReadOnlyCollection<Assembly> assemblies)
    {
        // Only an assembly that references FluentValidation can hold a validator: the others aren't scanned.
        var withValidators = assemblies
            .Where(ReferencesFluentValidation)
            .ToList();
        RegisterValidatorsOf(services, withValidators);
    }

    private static bool ReferencesFluentValidation(Assembly assembly)
        => assembly
            .GetReferencedAssemblies()
            .Any(reference => reference.Name == FluentValidationAssemblyName);

    private const string FluentValidationAssemblyName = "FluentValidation";

    private static void RegisterValidatorsOf(
        IServiceCollection services,
        List<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var validator in AssemblyScanner.FindValidatorsInAssembly(assembly, includeInternalTypes: true))
            {
                services.TryAddEnumerable(
                    ServiceDescriptor.Scoped(validator.InterfaceType, validator.ValidatorType));
                services.TryAdd(
                    ServiceDescriptor.Scoped(validator.ValidatorType, validator.ValidatorType));
            }
        }
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
        ServiceLifetime mediatorLifetime)
    {
        MediatorConfigurationValidation.ValidateMediatorLifetime(mediatorLifetime, nameof(mediatorLifetime));

        services.AddNotificationPublisher(publishStrategy);
        // The clock the logging behavior reads; replace it (e.g. with FakeTimeProvider) in tests.
        services.TryAddSingleton(TimeProvider.System);

        // A request's pipeline, composed by the container (the one runtime): transient, as each mediator keeps the
        // one it built (one slot per kind of call), so scoping it only cost every scope its tracking dictionary.
        services.TryAddTransient(typeof(IDiComposedSendRuntime<,>), typeof(DiComposedSendRuntime<,>));
        services.TryAddTransient(typeof(IDiComposedStreamRuntime<,>), typeof(DiComposedStreamRuntime<,>));

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
