using System;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// Tuning knobs for the outbox processing job, surfaced through
/// <see cref="OutboxServiceCollectionExtensions.AddOutboxProcessing{TDbContext}"/>.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>The default of <see cref="BacklogCheckInterval"/>: 30 s.</summary>
    internal static readonly TimeSpan DefaultBacklogCheckInterval = TimeSpan.FromSeconds(30);

    /// <summary>Maximum number of messages claimed per processing cycle (at least 1).</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// Time between processing cycles (60 s by default; at least 1 s). A run drains a backlog (it reads batch after
    /// batch while they come back full) for up to 80% of it.
    /// </summary>
    public TimeSpan ProcessInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Time after start-up before the first cycle (60 s by default; not negative).</summary>
    public TimeSpan DelayBeforeStart { get; set; } = TimeSpan.FromSeconds(60);

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
    /// How long a processed message is kept before the hourly cleanup deletes it (7 days by default). Unprocessed
    /// messages and messages that gave up are always kept. <see cref="TimeSpan.Zero"/> turns the cleanup off.
    /// </summary>
    public TimeSpan ProcessedRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Time between measurements of the backlog (waiting and given-up messages, the oldest one's age) for the
    /// <c>outbox.messages.*</c> gauges and the outbox health check (30 s by default; at least 1 s).
    /// </summary>
    public TimeSpan BacklogCheckInterval { get; set; } = DefaultBacklogCheckInterval;

    /// <summary>
    /// Claims each message for its processing transaction, so several service instances don't each publish it: the
    /// outbox job's and the outbox lanes' (scatter-gather included), unless <c>AddOutboxLanes</c> sets a claim of its
    /// own. <see langword="null"/> (the default): the PostgreSQL claim (<c>PostgresOutboxLaneClaim</c>)
    /// when the context writes its outbox through the Postgres package (<c>AddPostgresDbContext(..., db =&gt;
    /// db.WithOutbox())</c>), no claim otherwise (fine for one instance).
    /// </summary>
    public IOutboxLaneClaim? Claim { get; set; }
}
