using System;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

public sealed class OutboxMessage
{
    /// <summary>
    /// The <see cref="ProcessedOnUtc"/> of a message that gave up (failed its last try): 9999-12-31 UTC, never a real
    /// processing time. It takes the message out of the unprocessed messages (<c>ProcessedOnUtc IS NULL</c>, and so out
    /// of their index, which the polls read), never reaches the cleanup's cutoff (the message is kept), and is found
    /// by the processed index (<c>ProcessedOnUtc = '9999-12-31'</c>). <see cref="Error"/> says when and why it gave up.
    /// </summary>
    /// <remarks>
    /// To try a given-up message again: <c>UPDATE "OutboxMessages" SET "ProcessedOnUtc" = NULL, "RetryCount" = 0
    /// WHERE "Id" = ...</c>.
    /// </remarks>
    public static readonly DateTime GivenUpProcessedOnUtc = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime OccurredOnUtc { get; set; }

    /// <summary>
    /// When the message was processed; <see langword="null"/> while it waits (or is retried), and
    /// <see cref="GivenUpProcessedOnUtc"/> once it gave up.
    /// </summary>
    public DateTime? ProcessedOnUtc { get; set; }

    public string? Error { get; set; }

    public int RetryCount { get; set; }

    /// <summary>
    /// When an outbox lane may try the message again after a failed try; <see langword="null"/> when it may be tried at
    /// once (never tried, or not in a lane). Kept in the database, so every instance running the lanes waits it out.
    /// </summary>
    public DateTime? NextAttemptOnUtc { get; set; }

    /// <summary>
    /// Composed dedup key for events implementing
    /// <see cref="Resrcify.SharedKernel.Abstractions.DomainDrivenDesign.IDedupable"/>.
    /// NULL when the source event is not dedupable. Format:
    /// <c>{event.GetType().FullName}:{IDedupable.DedupKey}</c>.
    /// </summary>
    public string? DedupKey { get; set; }
}