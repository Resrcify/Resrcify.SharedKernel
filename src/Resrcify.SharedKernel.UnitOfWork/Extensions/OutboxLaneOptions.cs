using System;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>Tuning for outbox lanes, set through <see cref="OutboxLaneServiceCollectionExtensions.AddOutboxLanes{TDbContext}"/>.</summary>
public sealed class OutboxLaneOptions
{
    /// <summary>
    /// Messages each lane processes at once. A scatter-gather message holds a database connection and
    /// an open transaction while it waits for replies, so this also bounds the connections a lane uses.
    /// </summary>
    public int MaxConcurrency { get; set; } = 8;

    /// <summary>How often each lane looks for new messages when it has free slots.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The attempts a lane message gets when a try fails for a reason another try won't change (a handler's bug, a 400),
    /// and the waits between them: 3 attempts, 5 s after the first failure, then twice as long each time, up to 5 min,
    /// by default. When the last attempt fails, it gives up and is no longer polled. The wait is written to
    /// <c>OutboxMessage.NextAttemptOnUtc</c> with the failure, so every instance waits it out, and every lane skips the
    /// message until then; a <see cref="RetrySchedule.FirstDelay"/> of <see cref="TimeSpan.Zero"/> tries it again at the
    /// lane's next poll. A transient failure gets <see cref="TransientRetries"/>.
    /// </summary>
    public RetrySchedule Retries { get; set; } = OutboxRetryLimits.DefaultRetries;

    /// <summary>
    /// The attempts and waits a lane message gets when a try fails for a reason another try may get past (the
    /// container's <c>ITransientFailureClassifier</c>: a <c>TransientFailureException</c>, a cancellation or timeout, a
    /// transient database failure, an HTTP request that got no answer or a 408, 429 or 5xx, or what a registered
    /// <c>ITransientFailureRule</c> calls transient). Unset (<see langword="null"/>, the default): <see cref="Retries"/>
    /// with 12 attempts, about 30 minutes with the default delays, or <see cref="Retries"/>' attempts when that is
    /// higher. Set, at least <see cref="Retries"/>' attempts, or registration throws; as many gives every failure the same
    /// attempts (as before 4.3.0). A non-transient failure after transient retries past <see cref="Retries"/>' attempts
    /// gives up at once.
    /// </summary>
    public RetrySchedule? TransientRetries { get; set; }

    /// <summary>
    /// Claims each message for its processing transaction, so several service instances can run the
    /// same lane safely. <see langword="null"/> (no claim) is fine for a single instance; use
    /// <see cref="PostgresOutboxLaneClaim.Instance"/> on PostgreSQL.
    /// </summary>
    public IOutboxLaneClaim? Claim { get; set; }
}
