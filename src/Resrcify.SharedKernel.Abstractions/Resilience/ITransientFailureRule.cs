using System;

namespace Resrcify.SharedKernel.Abstractions.Resilience;

/// <summary>
/// A rule of a package's or a service's own that an exception is transient (another try may get past it), on top of
/// SharedKernel's built-in one (<c>TransientFailures.IsTransient</c>): e.g. the Web package's for its resilience
/// pipeline's rejections, or UnitOfWork's for a concurrency conflict. Register it with
/// <see cref="TransientFailureServiceCollectionExtensions.AddTransientFailureRule{TRule}"/>; the
/// <see cref="ITransientFailureClassifier"/> ORs every registered rule with the built-in one, so a rule can only add
/// transience, never take it away.
/// </summary>
public interface ITransientFailureRule
{
    /// <summary>
    /// Whether <paramref name="exception"/> itself is transient. Its causes are asked one by one: the rule needn't walk
    /// <see cref="Exception.InnerException"/>.
    /// </summary>
    /// <param name="exception">The exception, or one of its causes.</param>
    bool IsTransient(Exception exception);
}
