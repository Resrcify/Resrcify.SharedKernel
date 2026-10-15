using System;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// The tries an outbox message gets, for the processing job and the lanes alike, and the one place that decides them:
/// <see cref="MaxRetryCount"/> when a try fails for a reason another try won't change, <see cref="MaxTransientRetryCount"/>
/// when another try may pass (<see cref="OutboxFailureClassifier.IsTransient"/>: a downstream outage, a timeout). A
/// failure gives up when its try reaches the limit for that failure (<see cref="For"/>), so a message that retried
/// transiently past <see cref="MaxRetryCount"/> gives up at its first non-transient failure.
/// </summary>
/// <remarks>
/// Made only by <see cref="Create"/>, which normalises and validates once, so <see cref="MaxTransientRetryCount"/> is
/// never below <see cref="MaxRetryCount"/>.
/// </remarks>
internal sealed record OutboxRetryLimits
{
    /// <summary>The default of <see cref="MaxRetryCount"/>: 3.</summary>
    public const int DefaultMaxRetryCount = 3;

    /// <summary>
    /// The default of <see cref="MaxTransientRetryCount"/>: 12, about 30 minutes with the default backoff, or
    /// <see cref="MaxRetryCount"/> when that is higher.
    /// </summary>
    public const int DefaultMaxTransientRetryCount = 12;

    private OutboxRetryLimits(int maxRetryCount, int maxTransientRetryCount)
    {
        MaxRetryCount = maxRetryCount;
        MaxTransientRetryCount = maxTransientRetryCount;
    }

    /// <summary>The limits by default: 3 tries, 12 for a transient failure.</summary>
    public static OutboxRetryLimits Default { get; } = Create(DefaultMaxRetryCount, maxTransientRetryCount: null);

    /// <summary>The tries a message gets when it fails for a non-transient reason: at least 1.</summary>
    public int MaxRetryCount { get; }

    /// <summary>The tries a message gets when it keeps failing for a transient reason: at least <see cref="MaxRetryCount"/>.</summary>
    public int MaxTransientRetryCount { get; }

    /// <summary>
    /// The limit a message's tries are read and counted against: <see cref="MaxTransientRetryCount"/>, the higher of the
    /// two, so a message retrying transiently past <see cref="MaxRetryCount"/> is still read, and isn't counted as given
    /// up.
    /// </summary>
    public int ReadLimit => MaxTransientRetryCount;

    /// <summary>
    /// The limits for <paramref name="maxRetryCount"/> and <paramref name="maxTransientRetryCount"/>. A transient count
    /// left unset (<see langword="null"/>) is <see cref="DefaultMaxTransientRetryCount"/>, or
    /// <paramref name="maxRetryCount"/> when that is higher.
    /// </summary>
    /// <param name="maxRetryCountName">The name an exception gives <paramref name="maxRetryCount"/>.</param>
    /// <param name="maxTransientRetryCountName">The name an exception gives <paramref name="maxTransientRetryCount"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxRetryCount"/> is below 1, or <paramref name="maxTransientRetryCount"/> is set below it.
    /// </exception>
    public static OutboxRetryLimits Create(
        int maxRetryCount,
        int? maxTransientRetryCount,
        string maxRetryCountName = nameof(MaxRetryCount),
        string maxTransientRetryCountName = nameof(MaxTransientRetryCount))
    {
        if (maxRetryCount < 1)
            throw new ArgumentOutOfRangeException(
                maxRetryCountName,
                maxRetryCount,
                "A message gets at least one try.");

        if (maxTransientRetryCount is not { } transient)
            return new OutboxRetryLimits(maxRetryCount, Math.Max(DefaultMaxTransientRetryCount, maxRetryCount));

        if (transient < maxRetryCount)
            throw new ArgumentOutOfRangeException(
                maxTransientRetryCountName,
                transient,
                $"A transient failure can't get fewer tries than any other failure ({maxRetryCount}); set it to "
                + $"{maxRetryCount} to give every failure the same tries, or leave it unset for "
                + $"{Math.Max(DefaultMaxTransientRetryCount, maxRetryCount)}.");

        return new OutboxRetryLimits(maxRetryCount, transient);
    }

    /// <summary>The tries <paramref name="failure"/> gets, and whether it is transient.</summary>
    public OutboxFailureLimit For(Exception failure)
    {
        var transient = OutboxFailureClassifier.IsTransient(failure);
        return new OutboxFailureLimit(
            transient
                ? MaxTransientRetryCount
                : MaxRetryCount,
            transient);
    }
}

/// <summary>The tries a failed try's failure gets (<see cref="OutboxRetryLimits.For"/>).</summary>
/// <param name="MaxTries">The tries a failure of this kind gets.</param>
/// <param name="Transient">Whether another try may pass (<see cref="OutboxFailureClassifier.IsTransient"/>).</param>
internal readonly record struct OutboxFailureLimit(int MaxTries, bool Transient)
{
    /// <summary>Whether a failure on try <paramref name="attempt"/> (1 for the first) gives up.</summary>
    public bool GivesUpAt(int attempt)
        => attempt >= MaxTries;
}
