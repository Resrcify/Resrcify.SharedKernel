using System;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.Messaging.Configuration;

internal static class MediatorConfigurationValidation
{
    internal static void ValidateLifetime(ServiceLifetime lifetime, string paramName)
    {
        if (!Enum.IsDefined(lifetime))
            throw new ArgumentOutOfRangeException(paramName, lifetime, "Invalid service lifetime value.");
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
                openInterface != typeof(IValueTaskRequestPipelineBehavior<,>))
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
}
