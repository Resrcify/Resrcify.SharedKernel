using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.Messaging.Extensions;

internal static class OpenBehaviorServiceCollectionExtensions
{
    private static readonly Type[] OpenBehaviorServiceTypes =
    [
        typeof(IPipelineBehavior<,>),
        typeof(IRequestPipelineBehavior<,>),
        typeof(IValueTaskPipelineBehavior<,>),
        typeof(IValueTaskRequestPipelineBehavior<,>)
    ];

    internal static IEnumerable<Type> GetOpenBehaviorServiceTypes(Type behaviorType)
    {
        foreach (var implementedInterface in behaviorType.GetInterfaces())
        {
            if (!implementedInterface.IsGenericType)
                continue;

            var openInterface = implementedInterface.GetGenericTypeDefinition();
            for (var index = 0; index < OpenBehaviorServiceTypes.Length; index++)
            {
                if (OpenBehaviorServiceTypes[index] == openInterface)
                {
                    yield return openInterface;
                    break;
                }
            }
        }
    }

    internal static void RemoveMatchingOpenBehaviorRegistration(
        this IServiceCollection services,
        Type serviceType,
        Type implementationType)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType != serviceType)
                continue;

            if (descriptor.ImplementationType != implementationType)
                continue;

            services.RemoveAt(index);
        }
    }
}
