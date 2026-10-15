using System;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.Primitives;

/// <summary>
/// UnitOfWork's <see cref="ITransientFailureRule"/>: a save the database refused in a way another try may get past
/// (<see cref="PersistenceErrors"/>: a concurrency conflict, which a try in a new scope reads again; a serialization
/// failure or a deadlock, even when the driver doesn't flag them <c>IsTransient</c>). Registered by the outbox
/// registrations (<c>AddOutboxProcessing</c>, <c>AddOutboxLanes</c>, <c>AddProcessOutboxMessagesJob</c>).
/// </summary>
internal sealed class PersistenceTransientFailureRule
    : ITransientFailureRule
{
    public bool IsTransient(Exception exception)
        => Matches(exception);

    /// <summary>The rule, for a caller without a classifier in its container.</summary>
    public static bool Matches(Exception exception)
        => PersistenceErrors.TryGetError(exception, inTransaction: false, out var error) && error.IsTransient();

    /// <summary>
    /// Whether <paramref name="exception"/> is transient: by <paramref name="classifier"/> when there is one (it holds
    /// this rule when an outbox registration registered it), else by the built-in rule and this one.
    /// </summary>
    public static bool Classify(
        Exception exception,
        ITransientFailureClassifier? classifier)
        => classifier?.IsTransient(exception)
            ?? TransientFailures.IsTransient(exception, Matches);
}
