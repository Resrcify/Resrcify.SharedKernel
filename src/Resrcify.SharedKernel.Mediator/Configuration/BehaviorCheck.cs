using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Extensions;

namespace Resrcify.SharedKernel.Mediator.Configuration;

/// <summary>
/// Checks, when the mediator is registered, that every request asking for a behavior by its interface has one: a
/// request implementing <see cref="ICachingQuery"/> needs a behavior that caches (<see cref="CachingPipelineBehavior{TRequest, TResponse}"/>),
/// one implementing <see cref="ITransactionalCommand"/> a behavior that opens the transaction
/// (<see cref="TransactionPipelineBehavior{TRequest, TResponse}"/>). Without it the request still runs, just not cached
/// or not in a transaction, and nothing says so: a mistake found only in production. So it fails the registration
/// instead (an exception at start-up, which every test that builds the service's container sees) rather than logging a
/// warning nobody reads.
/// </summary>
/// <remarks>
/// A behavior handles an interface when its request type parameter is constrained to it (or to an interface deriving
/// from it), as the SharedKernel ones are, so a service's own caching behavior counts too. It must also run for the
/// request's handler: a <see cref="Task"/> handler (<see cref="IRequestHandler{TRequest, TResponse}"/>) runs only the
/// Task behaviors (<see cref="IPipelineBehavior{TRequest, TResponse}"/>, <see cref="IRequestPipelineBehavior{TRequest, TResponse}"/>),
/// a ValueTask handler only the ValueTask ones. It looks at the behaviors added to the configuration and those
/// registered in the container before <c>AddMediator</c>; one registered after can't be seen. <see cref="IBaseCommand"/>
/// is not checked: every command implements it, and a service without a unit of work rightly has no behavior for it.
/// </remarks>
internal static class BehaviorCheck
{
    /// <summary>What to do when the behavior is registered after <c>AddMediator(cfg => ...)</c>.</summary>
    internal const string ConfigurationRemedy =
        "If a behavior registered after AddMediator handles them, call cfg.SkipBehaviorCheck().";

    /// <summary>What to do when the behavior is registered after <c>AddMediator(assemblies)</c>, which has no options.</summary>
    internal const string AssembliesRemedy =
        "If you register it yourself, do so before AddMediator(assemblies), or register the mediator with "
        + "AddMediator(cfg => cfg.RegisterServicesFromAssemblies(...).AddStandardBehaviors()) (cfg.SkipBehaviorCheck() "
        + "turns the check off there).";

    private static readonly (Type Interface, Type Behavior)[] Required =
    [
        (typeof(ICachingQuery), typeof(CachingPipelineBehavior<,>)),
        (typeof(ITransactionalCommand), typeof(TransactionPipelineBehavior<,>)),
    ];

    private static readonly Dictionary<Type, PipelineKind> BehaviorKinds = new()
    {
        [typeof(IPipelineBehavior<,>)] = PipelineKind.Task,
        [typeof(IRequestPipelineBehavior<,>)] = PipelineKind.Task,
        [typeof(IValueTaskPipelineBehavior<,>)] = PipelineKind.ValueTask,
        [typeof(IValueTaskRequestPipelineBehavior<,>)] = PipelineKind.ValueTask,
    };

    private static readonly Dictionary<Type, PipelineKind> HandlerKinds = new()
    {
        [typeof(IRequestHandler<,>)] = PipelineKind.Task,
        [typeof(IValueTaskRequestHandler<,>)] = PipelineKind.ValueTask,
    };

    /// <summary>Which pipeline a request runs through: its handler's kind decides which behaviors run.</summary>
    internal enum PipelineKind
    {
        Task,
        ValueTask,
    }

    /// <summary>Checks the requests <paramref name="assemblies"/> have handlers for.</summary>
    /// <exception cref="InvalidOperationException">A request asks for a behavior that isn't registered.</exception>
    public static void Run(
        IServiceCollection services,
        IReadOnlyCollection<Assembly> assemblies,
        IEnumerable<Type> configuredBehaviorTypes,
        string remedy)
        => RunFor(
            services,
            HandledRequests(assemblies),
            configuredBehaviorTypes,
            remedy);

    /// <summary>Checks <paramref name="requests"/>, each with the kind of handler it has.</summary>
    /// <exception cref="InvalidOperationException">A request asks for a behavior that isn't registered.</exception>
    internal static void RunFor(
        IServiceCollection services,
        IReadOnlyCollection<(Type Request, PipelineKind Kind)> requests,
        IEnumerable<Type> configuredBehaviorTypes,
        string remedy = ConfigurationRemedy)
    {
        if (requests.Count == 0)
            return;

        var behaviors = configuredBehaviorTypes
            .SelectMany(type => OpenBehaviorServiceCollectionExtensions.GetOpenBehaviorServiceTypes(type)
                .Where(BehaviorKinds.ContainsKey)
                .Select(serviceType => (Type: type, Kind: BehaviorKinds[serviceType])))
            .Concat(RegisteredOpenBehaviors(services))
            .ToList();

        foreach (var (requiredInterface, behavior) in Required)
        {
            foreach (var kind in Enum.GetValues<PipelineKind>())
            {
                var asking = requests
                    .Where(request => request.Kind == kind && requiredInterface.IsAssignableFrom(request.Request))
                    .Select(request => request.Request)
                    .ToList();
                var handled = behaviors.Exists(registered => registered.Kind == kind
                    && Handles(registered.Type, requiredInterface));
                if (asking.Count > 0 && !handled)
                    throw new InvalidOperationException(Describe(requiredInterface, behavior, kind, asking, remedy));
            }
        }
    }

    /// <summary>The requests the scanned assemblies have a handler for, with the handler's kind.</summary>
    internal static List<(Type Request, PipelineKind Kind)> HandledRequests(IReadOnlyCollection<Assembly> assemblies)
        => [.. assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(implemented => implemented.IsGenericType
                && HandlerKinds.ContainsKey(implemented.GetGenericTypeDefinition()))
            .Select(implemented => (
                Request: implemented.GetGenericArguments()[0],
                Kind: HandlerKinds[implemented.GetGenericTypeDefinition()]))
            .Distinct()];

    private static IEnumerable<(Type Type, PipelineKind Kind)> RegisteredOpenBehaviors(IServiceCollection services)
        => services
            .Where(descriptor => descriptor.ServiceType.IsGenericTypeDefinition
                && BehaviorKinds.ContainsKey(descriptor.ServiceType)
                && descriptor.ImplementationType is { IsGenericTypeDefinition: true })
            .Select(descriptor => (descriptor.ImplementationType!, BehaviorKinds[descriptor.ServiceType]));

    /// <summary>Whether <paramref name="behaviorType"/>'s request type parameter is constrained to <paramref name="requiredInterface"/>.</summary>
    private static bool Handles(Type behaviorType, Type requiredInterface)
        => behaviorType.IsGenericTypeDefinition
            && behaviorType.GetGenericArguments()[0]
                .GetGenericParameterConstraints()
                .Any(requiredInterface.IsAssignableFrom);

    private static string Describe(
        Type requiredInterface,
        Type behavior,
        PipelineKind kind,
        List<Type> asking,
        string remedy)
    {
        var examples = string.Join(", ", asking.Take(3).Select(type => type.Name));
        var more = asking.Count > 3 ? $" and {asking.Count - 3} more" : string.Empty;
        var behaviorName = behavior.Name[..behavior.Name.IndexOf('`', StringComparison.Ordinal)];
        if (kind == PipelineKind.ValueTask)
            return $"{asking.Count} request type(s) implement {requiredInterface.Name} ({examples}{more}) and have a "
                + "ValueTask handler (IValueTaskRequestHandler), but no ValueTask pipeline behavior "
                + $"(IValueTaskPipelineBehavior) handles {requiredInterface.Name}, so they would run without it: "
                + $"{behaviorName} and the other standard behaviors run only for Task handlers. Handle them with "
                + $"IRequestHandler, or add a ValueTask behavior that handles {requiredInterface.Name}. {remedy}";

        return $"{asking.Count} request type(s) implement {requiredInterface.Name} ({examples}{more}), but no registered "
            + $"pipeline behavior handles {requiredInterface.Name}, so they would run without it. Add {behaviorName} "
            + $"(cfg.AddStandardBehaviors(), or cfg.AddOpenBehavior(typeof({behaviorName}<,>))). {remedy}";
    }
}
