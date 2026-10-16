using System;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.UnitOfWork.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// The tries an outbox message gets, and the waits between them, for the processing job and the lanes alike, and the one
/// place that decides them: <see cref="Retries"/> when a try fails for a reason another try won't change,
/// <see cref="TransientRetries"/> when another try may pass (<see cref="ITransientFailureClassifier"/>: a downstream
/// outage, a timeout). A failure gives up when its try reaches the attempts of its schedule (<see cref="For"/>), so a
/// message that retried transiently past <see cref="Retries"/>' attempts gives up at its first non-transient failure.
/// </summary>
/// <remarks>
/// Made only by <see cref="Create"/>, which normalises and validates once, so <see cref="TransientRetries"/>' attempts
/// are never below <see cref="Retries"/>'.
/// </remarks>
internal sealed record OutboxRetryLimits
{
    /// <summary>The default attempts of <see cref="Retries"/>: 3.</summary>
    public const int DefaultMaxAttempts = 3;

    /// <summary>
    /// The default attempts of <see cref="TransientRetries"/>: 12, about 30 minutes with the default delays, or
    /// <see cref="Retries"/>' when that is higher.
    /// </summary>
    public const int DefaultTransientMaxAttempts = 12;

    private OutboxRetryLimits(
        RetrySchedule retries,
        RetrySchedule transientRetries)
    {
        Retries = retries;
        TransientRetries = transientRetries;
    }

    /// <summary>
    /// The schedule by default: 3 attempts, 5 s after the first failure, then twice as long each time, up to 5 min.
    /// </summary>
    public static RetrySchedule DefaultRetries { get; } = new(
        DefaultMaxAttempts,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(5));

    /// <summary>The limits by default: 3 attempts, 12 for a transient failure, 5 s doubling up to 5 min.</summary>
    public static OutboxRetryLimits Default { get; } = Create(DefaultRetries, transientRetries: null);

    /// <summary>The schedule of a message whose try failed for a non-transient reason.</summary>
    public RetrySchedule Retries { get; }

    /// <summary>The schedule of a message whose try failed for a transient reason: at least as many attempts.</summary>
    public RetrySchedule TransientRetries { get; }

    /// <summary>The attempts a message gets when it fails for a non-transient reason.</summary>
    public int MaxRetryCount => Retries.MaxAttempts;

    /// <summary>The attempts a message gets when it keeps failing for a transient reason.</summary>
    public int MaxTransientRetryCount => TransientRetries.MaxAttempts;

    /// <summary>
    /// The limit a message's tries are read and counted against: the transient attempts, the higher of the two, so a
    /// message retrying transiently past <see cref="MaxRetryCount"/> is still read, and isn't counted as given up.
    /// </summary>
    public int ReadLimit => MaxTransientRetryCount;

    /// <summary>
    /// The limits for <paramref name="retries"/> and <paramref name="transientRetries"/>. A transient schedule left unset
    /// (<see langword="null"/>) is <paramref name="retries"/> with <see cref="DefaultTransientMaxAttempts"/> attempts, or
    /// its own when that is higher.
    /// </summary>
    /// <param name="retriesName">The name an exception gives <paramref name="retries"/>.</param>
    /// <param name="transientRetriesName">The name an exception gives <paramref name="transientRetries"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="retries"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="transientRetries"/> is set with fewer attempts than <paramref name="retries"/>.
    /// </exception>
    public static OutboxRetryLimits Create(
        RetrySchedule retries,
        RetrySchedule? transientRetries,
        string retriesName = nameof(Retries),
        string transientRetriesName = nameof(TransientRetries))
    {
        if (retries is null)
            throw new ArgumentNullException(retriesName);

        var transientDefault = Math.Max(DefaultTransientMaxAttempts, retries.MaxAttempts);
        if (transientRetries is null)
            return new OutboxRetryLimits(retries, retries with { MaxAttempts = transientDefault });

        if (transientRetries.MaxAttempts < retries.MaxAttempts)
            throw new ArgumentOutOfRangeException(
                transientRetriesName,
                transientRetries.MaxAttempts,
                $"A transient failure can't get fewer attempts than any other failure ({retries.MaxAttempts}); set "
                + $"them to {retries.MaxAttempts} to give every failure the same attempts, or leave TransientRetries "
                + $"unset for {transientDefault}.");

        return new OutboxRetryLimits(retries, transientRetries);
    }

    /// <summary>
    /// The schedule <paramref name="failure"/> gets, and whether it is transient: by <paramref name="classifier"/> (the
    /// container's), or, without one, by the built-in rule and UnitOfWork's (<see cref="PersistenceTransientFailureRule"/>).
    /// </summary>
    public OutboxFailureLimit For(
        Exception failure,
        ITransientFailureClassifier? classifier)
    {
        var transient = PersistenceTransientFailureRule.Classify(failure, classifier);
        return new OutboxFailureLimit(
            transient
                ? TransientRetries
                : Retries,
            transient);
    }
}

/// <summary>The schedule a failed try's failure gets (<see cref="OutboxRetryLimits.For"/>).</summary>
/// <param name="Schedule">The attempts and waits a failure of this kind gets.</param>
/// <param name="Transient">Whether another try may pass (<see cref="ITransientFailureClassifier"/>).</param>
internal readonly record struct OutboxFailureLimit(RetrySchedule Schedule, bool Transient)
{
    /// <summary>The attempts a failure of this kind gets.</summary>
    public int MaxTries => Schedule.MaxAttempts;

    /// <summary>Whether a failure on try <paramref name="attempt"/> (1 for the first) gives up.</summary>
    public bool GivesUpAt(int attempt)
        => attempt >= MaxTries;

    /// <summary>
    /// The wait after a failure on try <paramref name="attempt"/> before the next one (written to
    /// <c>OutboxMessage.NextAttemptOnUtc</c>); <see langword="null"/> when there is none to wait: tried again at once.
    /// </summary>
    public TimeSpan? DelayAfter(int attempt)
    {
        var delay = Schedule.DelayAfter(Math.Max(attempt, 1));
        return delay > TimeSpan.Zero
            ? delay
            : null;
    }
}
