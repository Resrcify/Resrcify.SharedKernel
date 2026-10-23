using System;
using System.Linq;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.ArchitectureTesting.Handlers;

/// <summary>
/// The other types that run inside the mediator pipeline, around a request's handler: FluentValidation validators (the
/// validation behavior runs them) and an app's own pipeline behaviors and pre- and post-processors.
/// </summary>
internal static class PipelineParticipants
{
    /// <summary>FluentValidation's validator interface, by its full name: this package takes no FluentValidation dependency.</summary>
    private const string ValidatorInterface = "FluentValidation.IValidator`1";

    private static readonly Type[] BehaviorInterfaces =
    [
        typeof(IPipelineBehavior<,>),
        typeof(IValueTaskPipelineBehavior<,>),
        typeof(IRequestPipelineBehavior<,>),
        typeof(IValueTaskRequestPipelineBehavior<,>),
        typeof(IStreamPipelineBehavior<,>),
        typeof(IRequestPreProcessor<>),
        typeof(IRequestPostProcessor<,>),
    ];

    /// <summary>
    /// Whether <paramref name="type"/> is a concrete class implementing FluentValidation's <c>IValidator&lt;T&gt;</c>
    /// (an <c>AbstractValidator&lt;T&gt;</c> included).
    /// </summary>
    public static bool IsValidator(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type is { IsClass: true, IsAbstract: false }
            && type
                .GetInterfaces()
                .Where(contract => contract.IsGenericType)
                .Any(contract => string.Equals(contract.GetGenericTypeDefinition().FullName, ValidatorInterface, StringComparison.Ordinal));
    }

    /// <summary>Whether <paramref name="type"/> is a concrete pipeline behavior, pre-processor or post-processor.</summary>
    public static bool IsBehavior(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type is { IsClass: true, IsAbstract: false }
            && type
                .GetInterfaces()
                .Where(contract => contract.IsGenericType)
                .Any(contract => BehaviorInterfaces.Contains(contract.GetGenericTypeDefinition()));
    }
}
