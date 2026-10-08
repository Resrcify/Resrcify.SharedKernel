using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Publishing;

namespace Resrcify.SharedKernel.Mediator.Configuration;

public sealed class MediatorConfiguration
{
    private readonly List<Assembly> _assemblies = [];
    private readonly List<Type> _openBehaviorTypes = [];
    private readonly List<OpenBehaviorRegistration> _openBehaviorRegistrations = [];
    private readonly List<ServiceDescriptor> _processorRegistrations = [];

    /// <summary>The assemblies whose handlers are registered (behaviors and processors are registered explicitly).</summary>
    public IReadOnlyCollection<Assembly> Assemblies => _assemblies;

    public IReadOnlyCollection<Type> OpenBehaviorTypes => _openBehaviorTypes;

    /// <summary>The behaviors to register, in order: the first is the outermost.</summary>
    public IReadOnlyCollection<OpenBehaviorRegistration> OpenBehaviorRegistrations => _openBehaviorRegistrations;

    public NotificationPublishStrategy NotificationPublishStrategy { get; private set; } = NotificationPublishStrategy.Sequential;

    public bool UseDiTimePipelineComposition { get; private set; }

    public ServiceLifetime MediatorLifetime { get; private set; } = ServiceLifetime.Transient;

    /// <summary>Whether <see cref="ConfigureLogging"/> was called (its options then replace another call's defaults).</summary>
    internal bool LoggingConfigured { get; private set; }

    /// <summary>Whether <see cref="ConfigureUnitOfWork"/> was called.</summary>
    internal bool UnitOfWorkConfigured { get; private set; }

    /// <summary>Whether <see cref="ConfigureIdempotency"/> was called.</summary>
    internal bool IdempotencyConfigured { get; private set; }

    /// <summary>How <see cref="LoggingPipelineBehavior{TRequest, TResponse}"/> logs (see <see cref="ConfigureLogging"/>).</summary>
    public LoggingPipelineOptions LoggingOptions { get; } = new();

    /// <summary>How <see cref="UnitOfWorkPipelineBehavior{TRequest, TResponse}"/> saves (see <see cref="ConfigureUnitOfWork"/>).</summary>
    public UnitOfWorkPipelineOptions UnitOfWorkOptions { get; } = new();

    /// <summary>How <see cref="IdempotencyPipelineBehavior{TRequest, TResponse}"/> keeps results (see <see cref="ConfigureIdempotency"/>).</summary>
    public IdempotencyPipelineOptions IdempotencyOptions { get; } = new();

    /// <summary>
    /// Whether <c>AddMediator</c> checks that every request asking for a behavior by its interface (an
    /// <see cref="ICachingQuery"/>, an <see cref="ITransactionalCommand"/>) has one registered (see
    /// <see cref="SkipBehaviorCheck"/>).
    /// </summary>
    public bool ChecksBehaviors { get; private set; } = true;

    /// <summary>The pre- and post-processors to register, in order.</summary>
    internal IReadOnlyList<ServiceDescriptor> ProcessorRegistrations => _processorRegistrations;

    /// <summary>
    /// Registers the request handlers (<see cref="IRequestHandler{TRequest, TResponse}"/>,
    /// <see cref="IValueTaskRequestHandler{TRequest, TResponse}"/>, <see cref="IStreamRequestHandler{TRequest, TResponse}"/>)
    /// and notification handlers (<see cref="INotificationHandler{TNotification}"/>) of <paramref name="assemblies"/>.
    /// Behaviors and processors in them are not registered: add them with <see cref="AddOpenBehavior(Type)"/>,
    /// <see cref="AddStandardBehaviors"/>, <see cref="AddRequestPreProcessor(Type)"/> and
    /// <see cref="AddRequestPostProcessor(Type)"/>, so they run only when asked for, in the order asked for.
    /// </summary>
    public MediatorConfiguration RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
            _assemblies.Add(assembly);

        return this;
    }

    /// <summary>
    /// Adds an open generic behavior (<see cref="IPipelineBehavior{TRequest, TResponse}"/>,
    /// <see cref="IRequestPipelineBehavior{TRequest, TResponse}"/>, their ValueTask forms, or
    /// <see cref="IStreamPipelineBehavior{TRequest, TResponse}"/>) after those of its kind added before it: the first
    /// added is the outermost of its kind. The kinds don't interleave: every <see cref="IPipelineBehavior{TRequest, TResponse}"/>
    /// runs outside every <see cref="IRequestPipelineBehavior{TRequest, TResponse}"/> (likewise for the ValueTask
    /// forms), and a ValueTask behavior runs only for a ValueTask handler, a Task one only for a Task handler.
    /// </summary>
    public MediatorConfiguration AddOpenBehavior(Type behaviorType)
        => AddOpenBehavior(behaviorType, ServiceLifetime.Transient);

    /// <inheritdoc cref="AddOpenBehavior(Type)"/>
    public MediatorConfiguration AddOpenBehavior(Type behaviorType, ServiceLifetime lifetime)
    {
        MediatorConfigurationValidation.ValidateOpenBehaviorType(behaviorType);
        MediatorConfigurationValidation.ValidateLifetime(lifetime, nameof(lifetime));

        _openBehaviorTypes.Add(behaviorType);
        _openBehaviorRegistrations.Add(new OpenBehaviorRegistration(behaviorType, lifetime));

        return this;
    }

    /// <summary>
    /// Adds the standard behaviors, outermost first: Logging → Validation → Transaction → UnitOfWork → Caching (see
    /// <see cref="StandardBehavior"/>), after any behavior added before. <paramref name="configure"/> can leave one out
    /// or put the service's own behaviors between them.
    /// </summary>
    public MediatorConfiguration AddStandardBehaviors(Action<StandardBehaviorsOptions>? configure = null)
    {
        var options = new StandardBehaviorsOptions();
        configure?.Invoke(options);

        foreach (var behaviorType in options.BehaviorTypes())
            AddOpenBehavior(behaviorType);

        return this;
    }

    /// <summary>Sets how <see cref="LoggingPipelineBehavior{TRequest, TResponse}"/> logs.</summary>
    public MediatorConfiguration ConfigureLogging(Action<LoggingPipelineOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(LoggingOptions);
        LoggingConfigured = true;
        return this;
    }

    /// <summary>
    /// Sets how <see cref="UnitOfWorkPipelineBehavior{TRequest, TResponse}"/> saves, e.g.
    /// <c>cfg.ConfigureUnitOfWork(uow =&gt; uow.ReturnPersistenceFailures = true)</c>.
    /// </summary>
    public MediatorConfiguration ConfigureUnitOfWork(Action<UnitOfWorkPipelineOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(UnitOfWorkOptions);
        UnitOfWorkConfigured = true;
        return this;
    }

    /// <summary>
    /// Sets how <see cref="IdempotencyPipelineBehavior{TRequest, TResponse}"/> keeps results, e.g.
    /// <c>cfg.ConfigureIdempotency(idempotency =&gt; idempotency.Expiration = TimeSpan.FromHours(1))</c>.
    /// </summary>
    public MediatorConfiguration ConfigureIdempotency(Action<IdempotencyPipelineOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(IdempotencyOptions);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(IdempotencyOptions.Expiration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(IdempotencyOptions.InProgressTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(IdempotencyOptions.MaxKeyLength, 1);
        IdempotencyConfigured = true;
        return this;
    }

    /// <summary>
    /// Adds a pre-processor, run before the request's behaviors: an open generic type implementing
    /// <see cref="IRequestPreProcessor{TRequest}"/>, or a closed one (registered for each request it implements it for).
    /// </summary>
    public MediatorConfiguration AddRequestPreProcessor(Type processorType)
        => AddRequestPreProcessor(processorType, ServiceLifetime.Transient);

    /// <inheritdoc cref="AddRequestPreProcessor(Type)"/>
    public MediatorConfiguration AddRequestPreProcessor(Type processorType, ServiceLifetime lifetime)
        => AddProcessor(processorType, typeof(IRequestPreProcessor<>), lifetime);

    /// <summary>
    /// Adds a post-processor, run after the request's handler: an open generic type implementing
    /// <see cref="IRequestPostProcessor{TRequest, TResponse}"/>, or a closed one (registered for each request it
    /// implements it for).
    /// </summary>
    public MediatorConfiguration AddRequestPostProcessor(Type processorType)
        => AddRequestPostProcessor(processorType, ServiceLifetime.Transient);

    /// <inheritdoc cref="AddRequestPostProcessor(Type)"/>
    public MediatorConfiguration AddRequestPostProcessor(Type processorType, ServiceLifetime lifetime)
        => AddProcessor(processorType, typeof(IRequestPostProcessor<,>), lifetime);

    /// <summary>
    /// Turns off the check <c>AddMediator</c> makes: that every request implementing <see cref="ICachingQuery"/> or
    /// <see cref="ITransactionalCommand"/> has a behavior registered that handles it (one whose request type parameter
    /// is constrained to that interface, as <c>CachingPipelineBehavior</c> and <c>TransactionPipelineBehavior</c> are).
    /// Only for a service that registers that behavior after <c>AddMediator</c>, where the check can't see it.
    /// </summary>
    public MediatorConfiguration SkipBehaviorCheck()
    {
        ChecksBehaviors = false;
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
        MediatorConfigurationValidation.ValidateMediatorLifetime(lifetime, nameof(lifetime));
        MediatorLifetime = lifetime;
        return this;
    }

    private MediatorConfiguration AddProcessor(
        Type processorType,
        Type openServiceType,
        ServiceLifetime lifetime)
    {
        MediatorConfigurationValidation.ValidateLifetime(lifetime, nameof(lifetime));
        foreach (var serviceType in MediatorConfigurationValidation.ProcessorServiceTypes(processorType, openServiceType))
            _processorRegistrations.Add(ServiceDescriptor.Describe(serviceType, processorType, lifetime));

        return this;
    }
}
