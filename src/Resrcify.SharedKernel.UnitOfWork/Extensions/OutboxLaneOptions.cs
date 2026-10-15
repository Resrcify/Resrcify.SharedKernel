using System;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

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

    /// <summary>Attempts a lane message gets; when the last one fails, it gives up and is no longer polled.</summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// How long a message whose try failed waits before its next one; each further retry waits twice as long, up to
    /// <see cref="MaxRetryDelay"/>. 5 s by default (5 s, 10 s, 20 s, ...). The time is written to
    /// <c>OutboxMessage.NextAttemptOnUtc</c> with the failure, so every instance waits it out, and every lane skips the
    /// message until then. <see cref="TimeSpan.Zero"/> tries it again at the lane's next poll, without a wait.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The longest a message waits between two tries (see <see cref="RetryDelay"/>): 5 min by default.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Claims each message for its processing transaction, so several service instances can run the
    /// same lane safely. <see langword="null"/> (no claim) is fine for a single instance; use
    /// <see cref="PostgresOutboxLaneClaim.Instance"/> on PostgreSQL.
    /// </summary>
    public IOutboxLaneClaim? Claim { get; set; }
}
