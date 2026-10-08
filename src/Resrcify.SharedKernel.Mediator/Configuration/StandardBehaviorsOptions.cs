using System;
using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.Abstractions.Mediator;
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
        // Outside the transaction and the unit of work: a result is kept only once committed, and a repeat opens neither.
        StandardBehavior.Idempotency,
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
    /// Puts <paramref name="behaviorTypes"/> (open generic <see cref="IPipelineBehavior{TRequest, TResponse}"/>s, as the
    /// standard ones are; transient) just before <paramref name="anchor"/>, in the order given.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A type isn't an <see cref="IPipelineBehavior{TRequest, TResponse}"/>: another kind can't run between the standard
    /// behaviors (see <see cref="MediatorConfiguration.AddOpenBehavior(Type)"/>); add it with <c>AddOpenBehavior</c>.
    /// </exception>
    public StandardBehaviorsOptions InsertBefore(StandardBehavior anchor, params Type[] behaviorTypes)
        => Insert(_before, anchor, behaviorTypes);

    /// <summary>
    /// Puts <paramref name="behaviorTypes"/> (open generic <see cref="IPipelineBehavior{TRequest, TResponse}"/>s, as the
    /// standard ones are; transient) just after <paramref name="anchor"/>, in the order given.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A type isn't an <see cref="IPipelineBehavior{TRequest, TResponse}"/> (see <see cref="InsertBefore"/>).
    /// </exception>
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
        {
            MediatorConfigurationValidation.ValidateOpenBehaviorType(behaviorType);
            EnsureRunsAmongTheStandardBehaviors(behaviorType);
        }

        if (!inserted.TryGetValue(anchor, out var types))
        {
            types = [];
            inserted[anchor] = types;
        }

        types.AddRange(behaviorTypes);
        return this;
    }

    // The standard behaviors are IPipelineBehaviors; the runtime runs the request behaviors (IRequestPipelineBehavior)
    // inside all of them and the ValueTask ones only for ValueTask handlers, so such a type would land elsewhere than
    // asked, silently.
    private static void EnsureRunsAmongTheStandardBehaviors(Type behaviorType)
    {
        var runsAmongThem = behaviorType
            .GetInterfaces()
            .Any(implemented => implemented.IsGenericType
                && implemented.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));
        if (!runsAmongThem)
            throw new ArgumentException(
                $"{behaviorType.Name} isn't an IPipelineBehavior<,>, so it can't run between the standard behaviors " +
                "(an IRequestPipelineBehavior runs inside all of them, a ValueTask behavior only for ValueTask " +
                "handlers). Make it an IPipelineBehavior<,>, or add it with cfg.AddOpenBehavior.",
                nameof(behaviorType));
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
            StandardBehavior.Idempotency => typeof(IdempotencyPipelineBehavior<,>),
            _ => throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Not a standard behavior."),
        };

    private static void ValidateStandardBehavior(StandardBehavior behavior)
    {
        if (!Enum.IsDefined(behavior))
            throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Not a standard behavior.");
    }
}
