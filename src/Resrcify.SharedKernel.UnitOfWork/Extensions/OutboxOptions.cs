using System;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>
/// Tuning knobs for the outbox processing job, surfaced through
/// <see cref="OutboxServiceCollectionExtensions.AddOutboxProcessing{TDbContext}"/>.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>Maximum number of messages claimed per processing cycle (at least 1).</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Seconds between processing cycles.</summary>
    public int ProcessIntervalInSeconds { get; set; } = 60;

    /// <summary>Seconds to wait after startup before the first cycle.</summary>
    public int DelayInSecondsBeforeStart { get; set; } = 60;

    /// <summary>
    /// Attempts a message gets. When the last one fails, the message gives up: it is marked
    /// (<c>OutboxMessage.GivenUpProcessedOnUtc</c>) and no longer polled.
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Days a processed message is kept before the hourly cleanup deletes it (default 7). Unprocessed messages and
    /// messages that gave up are always kept. 0 turns the cleanup off.
    /// </summary>
    public int ProcessedRetentionInDays { get; set; } = 7;

    /// <summary>
    /// Seconds between measurements of the backlog (waiting and given-up messages, the oldest one's age) for the
    /// <c>outbox.messages.*</c> gauges and the outbox health check.
    /// </summary>
    public int BacklogCheckIntervalInSeconds { get; set; } = 30;

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
