using System;
using Resrcify.SharedKernel.Abstractions.Resilience;
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
    /// The attempts a message gets when a try fails for a reason another try won't change (a handler's bug, a 400), and
    /// the waits between them: 3 attempts, 5 s after the first failure, then twice as long each time, up to 5 min, by
    /// default. When the last attempt fails, the message gives up: it is marked
    /// (<c>OutboxMessage.GivenUpProcessedOnUtc</c>) and no longer polled. The wait is written to
    /// <c>OutboxMessage.NextAttemptOnUtc</c> with the failure, so every instance waits it out, and the processing job
    /// skips the message until then; a <see cref="RetrySchedule.FirstDelay"/> of <see cref="TimeSpan.Zero"/> tries it
    /// again at the next run, without a wait. A transient failure gets <see cref="TransientRetries"/>.
    /// </summary>
    public RetrySchedule Retries { get; set; } = OutboxRetryLimits.DefaultRetries;

    /// <summary>
    /// The attempts and waits a message gets when a try fails for a reason another try may get past (the container's
    /// <c>ITransientFailureClassifier</c>: a <c>TransientFailureException</c>, a cancellation or timeout, a transient
    /// database failure, an HTTP request that got no answer or a 408, 429 or 5xx, or what a registered
    /// <c>ITransientFailureRule</c> calls transient). Unset (<see langword="null"/>, the default): <see cref="Retries"/>
    /// with 12 attempts, about 30 minutes with the default delays, so an outage of a downstream service doesn't spend
    /// them, or <see cref="Retries"/>' attempts when that is higher. Set, at least <see cref="Retries"/>' attempts, or
    /// registration throws; as many gives every failure the same attempts (as before 4.3.0). A non-transient failure
    /// after transient retries past <see cref="Retries"/>' attempts gives up at once.
    /// </summary>
    public RetrySchedule? TransientRetries { get; set; }

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
}
