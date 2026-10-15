using System;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// The tries an outbox message gets, for the processing job and the lanes alike: <see cref="MaxRetryCount"/> when a try
/// fails for a reason another try won't change, <see cref="MaxTransientRetryCount"/> when another try may pass
/// (<see cref="OutboxProcessLog.IsTransient"/>: a downstream outage, a timeout). A failure gives up when its try reaches
/// the limit for that failure, so a message that retried transiently past <see cref="MaxRetryCount"/> gives up at its
/// first non-transient failure.
/// </summary>
/// <param name="MaxRetryCount">The tries a message gets when it fails for a non-transient reason.</param>
/// <param name="MaxTransientRetryCount">The tries a message gets when it keeps failing for a transient reason.</param>
internal readonly record struct OutboxRetryLimits(int MaxRetryCount, int MaxTransientRetryCount)
{
    /// <summary>The default of <see cref="MaxRetryCount"/>: 3.</summary>
    public const int DefaultMaxRetryCount = 3;

    /// <summary>The default of <see cref="MaxTransientRetryCount"/>: 12, about 30 minutes with the default backoff.</summary>
    public const int DefaultMaxTransientRetryCount = 12;

    /// <summary>
    /// The limit a message's tries are read and counted against: the higher of the two, so a message retrying
    /// transiently past <see cref="MaxRetryCount"/> is still read, and isn't counted as given up.
    /// </summary>
    public int ReadLimit => Math.Max(MaxRetryCount, MaxTransientRetryCount);

    /// <summary>
    /// Throws when <paramref name="maxRetryCount"/> is below 1 or <paramref name="maxTransientRetryCount"/> is below
    /// it.
    /// </summary>
    public static void Validate(
        int maxRetryCount,
        int maxTransientRetryCount,
        string maxRetryCountName,
        string maxTransientRetryCountName)
    {
        if (maxRetryCount < 1)
            throw new ArgumentOutOfRangeException(
                maxRetryCountName,
                maxRetryCount,
                "A message gets at least one try.");

        if (maxTransientRetryCount < maxRetryCount)
            throw new ArgumentOutOfRangeException(
                maxTransientRetryCountName,
                maxTransientRetryCount,
                $"A transient failure can't get fewer tries than any other failure ({maxRetryCount}); set it to "
                + $"{maxRetryCount} to give every failure the same tries.");
    }
}
