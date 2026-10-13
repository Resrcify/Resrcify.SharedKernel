using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Mediator.Configuration;

internal static class MediatorConfigurationValidation
{
    internal static void ValidateLifetime(ServiceLifetime lifetime, string paramName)
    {
        if (!Enum.IsDefined(lifetime))
            throw new ArgumentOutOfRangeException(paramName, lifetime, "Invalid service lifetime value.");
    }

    // The mediator resolves handlers from the provider it was created by: a singleton one would resolve them from
    // the root provider, so a handler's scoped services (its DbContext) would live as long as the application.
    internal static void ValidateMediatorLifetime(ServiceLifetime lifetime, string paramName)
    {
        ValidateLifetime(lifetime, paramName);
        if (lifetime == ServiceLifetime.Singleton)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                lifetime,
                "The mediator can't be a singleton: it would resolve handlers and their scoped services from the root provider. Use Transient (the default) or Scoped.");
        }
    }

    internal static void ValidateOpenBehaviorType(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (!behaviorType.IsGenericTypeDefinition)
            throw new ArgumentException("Behavior type must be an open generic type definition.", nameof(behaviorType));

        var implementsPipelineBehavior = false;

        foreach (var implementedInterface in behaviorType.GetInterfaces())
        {
            if (!implementedInterface.IsGenericType)
                continue;

            var openInterface = implementedInterface.GetGenericTypeDefinition();
            if (openInterface != typeof(IPipelineBehavior<,>) &&
                openInterface != typeof(IRequestPipelineBehavior<,>) &&
                openInterface != typeof(IValueTaskPipelineBehavior<,>) &&
                openInterface != typeof(IValueTaskRequestPipelineBehavior<,>) &&
                openInterface != typeof(IStreamPipelineBehavior<,>))
                continue;

            implementsPipelineBehavior = true;
            break;
        }

        if (!implementsPipelineBehavior)
        {
            throw new ArgumentException(
                $"Behavior type '{behaviorType.FullName}' does not implement a supported pipeline behavior interface.",
                nameof(behaviorType));
        }
    }

    /// <summary>
    /// The services a processor is registered as: <paramref name="openServiceType"/> itself for an open generic
    /// processor, else each closed form of it the processor implements.
    /// </summary>
    internal static List<Type> ProcessorServiceTypes(Type processorType, Type openServiceType)
    {
        ArgumentNullException.ThrowIfNull(processorType);
        if (processorType is not { IsClass: true, IsAbstract: false })
            throw new ArgumentException($"Processor type '{processorType.FullName}' must be a concrete class.", nameof(processorType));

        var serviceTypes = new List<Type>();
        foreach (var implementedInterface in processorType.GetInterfaces())
        {
            if (!implementedInterface.IsGenericType || implementedInterface.GetGenericTypeDefinition() != openServiceType)
                continue;

            if (!processorType.IsGenericTypeDefinition)
            {
                serviceTypes.Add(implementedInterface);
                continue;
            }

            // An open processor is registered as the open service, which DI closes over the processor's own type
            // parameters: they must be the interface's, in its order.
            if (implementedInterface.GetGenericArguments().SequenceEqual(processorType.GetGenericArguments()))
                serviceTypes.Add(openServiceType);
        }

        if (serviceTypes.Count == 0)
        {
            throw new ArgumentException(
                $"Processor type '{processorType.FullName}' does not implement {openServiceType.Name}"
                + (processorType.IsGenericTypeDefinition ? " over its own type parameters." : "."),
                nameof(processorType));
        }

        return serviceTypes;
    }
}
