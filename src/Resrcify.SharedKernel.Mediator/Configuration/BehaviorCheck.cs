using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Behaviors;

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
/// from it), as the SharedKernel ones are, so a service's own caching behavior counts too. It looks at the behaviors
/// added to the configuration and those registered in the container before <c>AddMediator</c>; one registered after
/// can't be seen (<see cref="MediatorConfiguration.SkipBehaviorCheck"/>). <see cref="IBaseCommand"/> is not checked:
/// every command implements it, and a service without a unit of work rightly has no behavior for it.
/// </remarks>
internal static class BehaviorCheck
{
    private static readonly (Type Interface, Type Behavior)[] Required =
    [
        (typeof(ICachingQuery), typeof(CachingPipelineBehavior<,>)),
        (typeof(ITransactionalCommand), typeof(TransactionPipelineBehavior<,>)),
    ];

    private static readonly HashSet<Type> BehaviorServiceTypes =
    [
        typeof(IPipelineBehavior<,>),
        typeof(IRequestPipelineBehavior<,>),
        typeof(IValueTaskPipelineBehavior<,>),
        typeof(IValueTaskRequestPipelineBehavior<,>),
    ];

    private static readonly HashSet<Type> RequestHandlerTypes =
    [
        typeof(IRequestHandler<,>),
        typeof(IValueTaskRequestHandler<,>),
    ];

    /// <summary>Checks the requests <paramref name="assemblies"/> have handlers for.</summary>
    /// <exception cref="InvalidOperationException">A request asks for a behavior that isn't registered.</exception>
    public static void Run(
        IServiceCollection services,
        IReadOnlyCollection<Assembly> assemblies,
        IEnumerable<Type> configuredBehaviorTypes)
        => RunFor(
            services,
            HandledRequestTypes(assemblies),
            configuredBehaviorTypes);

    /// <summary>Checks <paramref name="requestTypes"/>.</summary>
    /// <exception cref="InvalidOperationException">A request asks for a behavior that isn't registered.</exception>
    internal static void RunFor(
        IServiceCollection services,
        IReadOnlyCollection<Type> requestTypes,
        IEnumerable<Type> configuredBehaviorTypes)
    {
        if (requestTypes.Count == 0)
            return;

        var behaviorTypes = configuredBehaviorTypes
            .Concat(RegisteredOpenBehaviorTypes(services))
            .ToList();

        foreach (var (requiredInterface, behavior) in Required)
        {
            var asking = requestTypes
                .Where(requiredInterface.IsAssignableFrom)
                .ToList();
            if (asking.Count == 0 || behaviorTypes.Exists(type => Handles(type, requiredInterface)))
                continue;

            throw new InvalidOperationException(Describe(requiredInterface, behavior, asking));
        }
    }

    /// <summary>The requests the scanned assemblies have a handler for.</summary>
    internal static List<Type> HandledRequestTypes(IReadOnlyCollection<Assembly> assemblies)
        => [.. assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(implemented => implemented.IsGenericType
                && RequestHandlerTypes.Contains(implemented.GetGenericTypeDefinition()))
            .Select(implemented => implemented.GetGenericArguments()[0])
            .Distinct()];

    private static IEnumerable<Type> RegisteredOpenBehaviorTypes(IServiceCollection services)
        => services
            .Where(descriptor => descriptor.ServiceType.IsGenericTypeDefinition
                && BehaviorServiceTypes.Contains(descriptor.ServiceType)
                && descriptor.ImplementationType is { IsGenericTypeDefinition: true })
            .Select(descriptor => descriptor.ImplementationType!);

    /// <summary>Whether <paramref name="behaviorType"/>'s request type parameter is constrained to <paramref name="requiredInterface"/>.</summary>
    private static bool Handles(Type behaviorType, Type requiredInterface)
        => behaviorType.IsGenericTypeDefinition
            && behaviorType.GetGenericArguments()[0]
                .GetGenericParameterConstraints()
                .Any(requiredInterface.IsAssignableFrom);

    private static string Describe(Type requiredInterface, Type behavior, List<Type> asking)
    {
        var examples = string.Join(", ", asking.Take(3).Select(type => type.Name));
        var more = asking.Count > 3 ? $" and {asking.Count - 3} more" : string.Empty;
        var behaviorName = behavior.Name[..behavior.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{asking.Count} request type(s) implement {requiredInterface.Name} ({examples}{more}), but no registered "
            + $"pipeline behavior handles {requiredInterface.Name}, so they would run without it. Add {behaviorName} "
            + $"(cfg.AddStandardBehaviors(), or cfg.AddOpenBehavior(typeof({behaviorName}<,>))). If a behavior "
            + "registered after AddMediator handles them, call cfg.SkipBehaviorCheck().";
    }
}
