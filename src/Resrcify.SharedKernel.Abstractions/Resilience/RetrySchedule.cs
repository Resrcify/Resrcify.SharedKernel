using System;
using System.Security.Cryptography;

namespace Resrcify.SharedKernel.Abstractions.Resilience;

/// <summary>
/// How often something is tried and how long it waits between tries: <see cref="MaxAttempts"/> tries in all (the first
/// one included), the first retry after <see cref="FirstDelay"/>, each next one twice as long, never longer than
/// <see cref="MaxDelay"/>. SharedKernel's one backoff: the outbox, the message bus, the HTTP resilience pipeline and the
/// reconnects all wait by <see cref="DelayAfter(int)"/>, and every count of tries is attempts, never retries after the
/// first.
/// </summary>
/// <remarks>
/// <para>
/// Immutable: change one value with <c>with</c>, e.g. <c>options.Retries = options.Retries with { MaxAttempts = 6 }</c>.
/// <see cref="MaxAttempts"/> is at least 1, the delays aren't negative; a <see cref="MaxDelay"/> shorter than
/// <see cref="FirstDelay"/> caps every delay at it. <see cref="TimeSpan.Zero"/> as <see cref="FirstDelay"/> retries at
/// once.
/// </para>
/// <para>
/// With <see cref="Jitter"/>, each delay is spread at random between half of it and all of it (never above the doubled,
/// capped delay), so the clients of a failing service don't retry in step.
/// </para>
/// </remarks>
/// <param name="MaxAttempts">The tries in all, the first one included: at least 1 (1 never retries).</param>
/// <param name="FirstDelay">The wait after the first failed try.</param>
/// <param name="MaxDelay">The longest wait between two tries.</param>
/// <param name="Jitter">Whether each wait is spread at random (between half of it and all of it).</param>
public sealed record RetrySchedule(
    int MaxAttempts,
    TimeSpan FirstDelay,
    TimeSpan MaxDelay,
    bool Jitter = false)
{
    /// <summary>The tries in all, the first one included: at least 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below 1.</exception>
    public int MaxAttempts
    {
        get;
        init => field = AtLeastOne(value, nameof(MaxAttempts));
    } = AtLeastOne(MaxAttempts, nameof(MaxAttempts));

    /// <summary>The wait after the first failed try; each next one waits twice as long, up to <see cref="MaxDelay"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set negative.</exception>
    public TimeSpan FirstDelay
    {
        get;
        init => field = NotNegative(value, nameof(FirstDelay));
    } = NotNegative(FirstDelay, nameof(FirstDelay));

    /// <summary>The longest wait between two tries.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set negative.</exception>
    public TimeSpan MaxDelay
    {
        get;
        init => field = NotNegative(value, nameof(MaxDelay));
    } = NotNegative(MaxDelay, nameof(MaxDelay));

    /// <summary>Whether another try follows the failed try <paramref name="failedAttempt"/> (1 for the first).</summary>
    /// <param name="failedAttempt">The try that failed, 1 for the first.</param>
    public bool HasAttemptAfter(int failedAttempt)
        => failedAttempt < MaxAttempts;

    /// <summary>
    /// The wait after the failed try <paramref name="failedAttempt"/> (1 for the first) before the next one:
    /// <see cref="FirstDelay"/> doubled once for every try before it, at most <see cref="MaxDelay"/> (whatever the number
    /// of tries: it never overflows), spread at random between half of it and all of it with <see cref="Jitter"/>.
    /// </summary>
    /// <param name="failedAttempt">The try that failed, 1 for the first.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="failedAttempt"/> is below 1.</exception>
    public TimeSpan DelayAfter(int failedAttempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempt, 1);

        var delay = Doubled(failedAttempt);
        return Jitter
            ? Spread(delay)
            : delay;
    }

    // FirstDelay doubled (failedAttempt - 1) times, capped: it stops doubling once past half the cap, so it can't
    // overflow, and it takes at most 64 steps whatever failedAttempt is.
    private TimeSpan Doubled(int failedAttempt)
    {
        var cap = MaxDelay;
        var delay = FirstDelay < cap ? FirstDelay : cap;
        for (var doubled = 1; doubled < failedAttempt && delay > TimeSpan.Zero && delay < cap; doubled++)
        {
            delay = delay.Ticks > cap.Ticks / 2
                ? cap
                : delay * 2;
        }

        return delay;
    }

    // Between half of the delay and all of it.
    private static TimeSpan Spread(TimeSpan delay)
    {
        var half = delay.Ticks / 2;
        var rest = delay.Ticks - half;
        var fraction = RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue;
        var spread = (long)(rest * fraction);
        return TimeSpan.FromTicks(half + Math.Min(spread, rest));
    }

    private static int AtLeastOne(
        int value,
        string name)
        => value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "A schedule has at least one try.");

    private static TimeSpan NotNegative(
        TimeSpan value,
        string name)
        => value >= TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "A delay can't be negative (TimeSpan.Zero doesn't wait).");
}
