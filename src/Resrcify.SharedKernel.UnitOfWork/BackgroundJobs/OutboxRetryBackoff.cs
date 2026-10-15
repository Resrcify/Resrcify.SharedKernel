using System;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// How long a failed outbox message waits before its next try, for the processing job and the lanes alike: the first
/// retry waits <c>retryDelay</c>, each further one twice as long, up to <c>maxRetryDelay</c>. The wait is written to
/// <c>OutboxMessage.NextAttemptOnUtc</c> with the failure, so every instance waits it out.
/// </summary>
internal static class OutboxRetryBackoff
{
    /// <summary>The default first retry delay: 5 s.</summary>
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>The default longest retry delay: 5 min.</summary>
    public static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The wait after the <paramref name="failures"/>th failure: <paramref name="retryDelay"/> doubled for each failure
    /// before it, at most <paramref name="maxRetryDelay"/>. <see langword="null"/> when <paramref name="retryDelay"/> is
    /// zero: the message may be tried again at once.
    /// </summary>
    public static TimeSpan? Delay(
        int failures,
        TimeSpan retryDelay,
        TimeSpan maxRetryDelay)
    {
        if (retryDelay <= TimeSpan.Zero)
            return null;

        // Doubled at most until past the cap: 5 s doubled 39 times overflows a TimeSpan.
        var delay = retryDelay;
        for (var doubled = 1; doubled < failures && delay < maxRetryDelay; doubled++)
            delay *= 2;
        return delay < maxRetryDelay ? delay : maxRetryDelay;
    }

    /// <summary>
    /// Throws when <paramref name="retryDelay"/> is negative or <paramref name="maxRetryDelay"/> is shorter than it.
    /// </summary>
    public static void Validate(
        TimeSpan retryDelay,
        TimeSpan maxRetryDelay,
        string retryDelayName,
        string maxRetryDelayName)
    {
        if (retryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                retryDelayName,
                retryDelay,
                "The retry delay can't be negative (TimeSpan.Zero retries without a wait).");

        if (maxRetryDelay < retryDelay)
            throw new ArgumentOutOfRangeException(
                maxRetryDelayName,
                maxRetryDelay,
                $"The longest retry delay can't be shorter than the first ({retryDelay}).");
    }
}
