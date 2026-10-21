using System;
using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.ArchitectureTesting.Handlers;

/// <summary>Finds the feature steps (<see cref="IFeatureStep"/>) of an assembly and what about each breaks the convention.</summary>
internal static class FeatureSteps
{
    /// <summary>Whether <paramref name="type"/> is a concrete class implementing <see cref="IFeatureStep"/>.</summary>
    public static bool IsFeatureStep(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type is { IsClass: true, IsAbstract: false }
            && typeof(IFeatureStep).IsAssignableFrom(type);
    }

    /// <summary>
    /// What breaks the convention, described for a failure message: visible outside its assembly (<c>is public</c>), not
    /// sealed, or a constructor parameter, field or property that sends (<see cref="ISender"/>, <see cref="IMediator"/>)
    /// or a constructor parameter that is a request handler.
    /// </summary>
    public static IReadOnlyList<string> Violations(Type step)
    {
        ArgumentNullException.ThrowIfNull(step);

        var violations = new List<string>();
        if (step.IsVisible)
            violations.Add("is public (a step is internal)");
        if (!step.IsSealed)
            violations.Add("is not sealed");

        violations.AddRange(MediatorRequestHandlers.SenderDependencies(step));
        violations.AddRange(MediatorRequestHandlers.HandlerDependencies(step));
        return violations
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
