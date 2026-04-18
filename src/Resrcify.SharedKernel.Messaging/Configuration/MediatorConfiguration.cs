using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Messaging.Publishing;

namespace Resrcify.SharedKernel.Messaging.Configuration;

public sealed class MediatorConfiguration
{
    private readonly List<Assembly> _assemblies = [];
    private readonly List<Type> _openBehaviorTypes = [];
    private readonly List<OpenBehaviorRegistration> _openBehaviorRegistrations = [];

    public IReadOnlyCollection<Assembly> Assemblies => _assemblies;

    public IReadOnlyCollection<Type> OpenBehaviorTypes => _openBehaviorTypes;

    public IReadOnlyCollection<OpenBehaviorRegistration> OpenBehaviorRegistrations => _openBehaviorRegistrations;

    public NotificationPublishStrategy NotificationPublishStrategy { get; private set; } = NotificationPublishStrategy.Sequential;

    public bool UseDiTimePipelineComposition { get; private set; }

    public ServiceLifetime MediatorLifetime { get; private set; } = ServiceLifetime.Transient;

    public MediatorConfiguration RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
            _assemblies.Add(assembly);

        return this;
    }

    public MediatorConfiguration AddOpenBehavior(Type behaviorType)
        => AddOpenBehavior(behaviorType, ServiceLifetime.Transient);

    public MediatorConfiguration AddOpenBehavior(Type behaviorType, ServiceLifetime lifetime)
    {
        MediatorConfigurationValidation.ValidateOpenBehaviorType(behaviorType);
        MediatorConfigurationValidation.ValidateLifetime(lifetime, nameof(lifetime));

        _openBehaviorTypes.Add(behaviorType);
        _openBehaviorRegistrations.Add(new OpenBehaviorRegistration(behaviorType, lifetime));

        return this;
    }

    public MediatorConfiguration UseNotificationPublishStrategy(
        NotificationPublishStrategy strategy)
    {
        NotificationPublishStrategy = strategy;
        return this;
    }

    public MediatorConfiguration EnableDiTimePipelineComposition(bool enabled = true)
    {
        UseDiTimePipelineComposition = enabled;
        return this;
    }

    public MediatorConfiguration UseMediatorLifetime(ServiceLifetime lifetime)
    {
        MediatorConfigurationValidation.ValidateLifetime(lifetime, nameof(lifetime));
        MediatorLifetime = lifetime;
        return this;
    }
}
