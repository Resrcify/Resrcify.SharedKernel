using System;
using System.Collections.Generic;
using Resrcify.SharedKernel.Mediator.Behaviors;

namespace Resrcify.SharedKernel.Mediator.Configuration;

/// <summary>
/// Shapes the standard behavior set (<see cref="MediatorConfiguration.AddStandardBehaviors"/>): leave a behavior out,
/// or put a service's own behaviors before or after one of them.
/// </summary>
/// <example>
/// <code>
/// cfg.AddStandardBehaviors(standard => standard
///     .InsertAfter(StandardBehavior.Validation, typeof(GuestAuthPipelineBehavior&lt;,&gt;), typeof(SingleFlightPipelineBehavior&lt;,&gt;))
///     .Without(StandardBehavior.Caching));
/// </code>
/// gives Logging → Validation → GuestAuth → SingleFlight → Transaction → UnitOfWork.
/// </example>
public sealed class StandardBehaviorsOptions
{
    private static readonly StandardBehavior[] Order =
    [
        StandardBehavior.Logging,
        StandardBehavior.Validation,
        StandardBehavior.Transaction,
        StandardBehavior.UnitOfWork,
        StandardBehavior.Caching,
    ];

    private readonly HashSet<StandardBehavior> _without = [];
    private readonly Dictionary<StandardBehavior, List<Type>> _before = [];
    private readonly Dictionary<StandardBehavior, List<Type>> _after = [];

    /// <summary>Leaves <paramref name="behavior"/> out. Behaviors inserted before or after it keep their place.</summary>
    public StandardBehaviorsOptions Without(StandardBehavior behavior)
    {
        ValidateStandardBehavior(behavior);
        _without.Add(behavior);
        return this;
    }

    /// <summary>
    /// Puts <paramref name="behaviorTypes"/> (open generic behaviors, as <c>AddOpenBehavior</c> takes; transient) just
    /// before <paramref name="anchor"/>, in the order given.
    /// </summary>
    public StandardBehaviorsOptions InsertBefore(StandardBehavior anchor, params Type[] behaviorTypes)
        => Insert(_before, anchor, behaviorTypes);

    /// <summary>
    /// Puts <paramref name="behaviorTypes"/> (open generic behaviors, as <c>AddOpenBehavior</c> takes; transient) just
    /// after <paramref name="anchor"/>, in the order given.
    /// </summary>
    public StandardBehaviorsOptions InsertAfter(StandardBehavior anchor, params Type[] behaviorTypes)
        => Insert(_after, anchor, behaviorTypes);

    /// <summary>The behaviors to register, outermost first.</summary>
    internal IEnumerable<Type> BehaviorTypes()
    {
        foreach (var behavior in Order)
        {
            foreach (var before in InsertedAt(_before, behavior))
                yield return before;

            if (!_without.Contains(behavior))
                yield return TypeOf(behavior);

            foreach (var after in InsertedAt(_after, behavior))
                yield return after;
        }
    }

    private StandardBehaviorsOptions Insert(
        Dictionary<StandardBehavior, List<Type>> inserted,
        StandardBehavior anchor,
        Type[] behaviorTypes)
    {
        ValidateStandardBehavior(anchor);
        ArgumentNullException.ThrowIfNull(behaviorTypes);
        foreach (var behaviorType in behaviorTypes)
            MediatorConfigurationValidation.ValidateOpenBehaviorType(behaviorType);

        if (!inserted.TryGetValue(anchor, out var types))
        {
            types = [];
            inserted[anchor] = types;
        }

        types.AddRange(behaviorTypes);
        return this;
    }

    private static List<Type> InsertedAt(
        Dictionary<StandardBehavior, List<Type>> inserted,
        StandardBehavior anchor)
        => inserted.TryGetValue(anchor, out var types) ? types : [];

    private static Type TypeOf(StandardBehavior behavior)
        => behavior switch
        {
            StandardBehavior.Logging => typeof(LoggingPipelineBehavior<,>),
            StandardBehavior.Validation => typeof(ValidationPipelineBehavior<,>),
            StandardBehavior.Transaction => typeof(TransactionPipelineBehavior<,>),
            StandardBehavior.UnitOfWork => typeof(UnitOfWorkPipelineBehavior<,>),
            StandardBehavior.Caching => typeof(CachingPipelineBehavior<,>),
            _ => throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Not a standard behavior."),
        };

    private static void ValidateStandardBehavior(StandardBehavior behavior)
    {
        if (!Enum.IsDefined(behavior))
            throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Not a standard behavior.");
    }
}
