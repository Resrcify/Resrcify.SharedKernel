using System;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>
/// Tuning knobs for the outbox processing job, surfaced through
/// <see cref="OutboxServiceCollectionExtensions.AddOutboxProcessing{TDbContext}"/>.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>The default of <see cref="BacklogCheckIntervalInSeconds"/>.</summary>
    internal const int DefaultBacklogCheckIntervalInSeconds = 30;

    /// <summary>Maximum number of messages claimed per processing cycle (at least 1).</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Seconds between processing cycles.</summary>
    public int ProcessIntervalInSeconds { get; set; } = 60;

    /// <summary>Seconds to wait after startup before the first cycle.</summary>
    public int DelayInSecondsBeforeStart { get; set; } = 60;

    /// <summary>
    /// Attempts a message gets when a try fails for a reason another try won't change (a handler's bug, a 400). When the
    /// last one fails, the message gives up: it is marked (<c>OutboxMessage.GivenUpProcessedOnUtc</c>) and no longer
    /// polled. A transient failure gets <see cref="MaxTransientRetryCount"/>.
    /// </summary>
    public int MaxRetryCount { get; set; } = OutboxRetryLimits.DefaultMaxRetryCount;

    /// <summary>
    /// Attempts a message gets when a try fails for a reason another try may get past (a cancellation or
    /// timeout, a transient database failure, an HTTP request that got no answer or a 408, 429 or 5xx, Polly's timeout
    /// or open circuit): 12 by default, about 30 minutes with the default <see cref="RetryDelay"/> and
    /// <see cref="MaxRetryDelay"/>, so an outage of a downstream service doesn't spend them. At least
    /// <see cref="MaxRetryCount"/>; set it to <see cref="MaxRetryCount"/> to give every failure the same tries (as before
    /// 4.3.0). A non-transient failure after transient retries past <see cref="MaxRetryCount"/> gives up at once.
    /// </summary>
    public int MaxTransientRetryCount { get; set; } = OutboxRetryLimits.DefaultMaxTransientRetryCount;

    /// <summary>
    /// How long a message whose try failed waits before its next one; each further retry waits twice as long, up to
    /// <see cref="MaxRetryDelay"/>. 5 s by default (5 s, 10 s, 20 s, ...). The time is written to
    /// <c>OutboxMessage.NextAttemptOnUtc</c> with the failure, so every instance waits it out, and the processing job skips the
    /// message until then. <see cref="TimeSpan.Zero"/> tries it again at its next run (as before 4.3.0), without a wait.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The longest a message waits between two tries (see <see cref="RetryDelay"/>): 5 min by default.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Days a processed message is kept before the hourly cleanup deletes it (default 7). Unprocessed messages and
    /// messages that gave up are always kept. 0 turns the cleanup off.
    /// </summary>
    public int ProcessedRetentionInDays { get; set; } = 7;

    /// <summary>
    /// Seconds between measurements of the backlog (waiting and given-up messages, the oldest one's age) for the
    /// <c>outbox.messages.*</c> gauges and the outbox health check.
    /// </summary>
    public int BacklogCheckIntervalInSeconds { get; set; } = DefaultBacklogCheckIntervalInSeconds;

    /// <summary>
    /// Claims each message for its processing transaction, so several service instances don't each publish it: the
    /// outbox job's and the outbox lanes' (scatter-gather included), unless <c>AddOutboxLanes</c> sets a claim of its
    /// own. <see langword="null"/> (no claim) is fine for one instance; use <c>PostgresOutboxLaneClaim.Instance</c> on
    /// PostgreSQL.
    /// </summary>
    public IOutboxLaneClaim? Claim { get; set; }

    /// <summary>
    /// The clock the jobs' first runs are scheduled on. <see langword="null"/>: the <see cref="System.TimeProvider"/>
    /// registered before <c>AddOutboxProcessing</c> (e.g. a <c>FakeTimeProvider</c> in a test), else the system clock.
    /// Quartz runs the schedule on the container's clock, so the two must be the same for a fake one to drive it.
    /// </summary>
    public TimeProvider? TimeProvider { get; set; }

    /// <summary>The two retry limits, together.</summary>
    internal OutboxRetryLimits RetryLimits => new(MaxRetryCount, MaxTransientRetryCount);
}
