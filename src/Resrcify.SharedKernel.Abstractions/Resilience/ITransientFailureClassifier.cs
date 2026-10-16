using System;

namespace Resrcify.SharedKernel.Abstractions.Resilience;

/// <summary>
/// Whether an exception is transient (another try may get past it): SharedKernel's built-in rule
/// (<c>TransientFailures.IsTransient</c>: a <c>TransientFailureException</c>, a timeout, a handler's own cancellation, a
/// transient database failure, an HTTP request without an answer or with a 408, 429 or 5xx) OR any registered
/// <see cref="ITransientFailureRule"/>, for the exception and every one of its causes. The outbox gives a transient
/// failure its transient tries by it, and the mediator's behaviors, the jobs and the message bus pick the level they log
/// an exception at by it. Registered by
/// <see cref="TransientFailureServiceCollectionExtensions.TryAddTransientFailureClassifier"/> (and by every
/// <c>AddTransientFailureRule</c>); a package that finds none in the container falls back to the built-in rule.
/// </summary>
public interface ITransientFailureClassifier
{
    /// <summary>Whether another try may get past <paramref name="exception"/>.</summary>
    /// <param name="exception">The exception.</param>
    bool IsTransient(Exception exception);
}
