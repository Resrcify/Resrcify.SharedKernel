using System;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime OccurredOnUtc { get; set; }

    public DateTime? ProcessedOnUtc { get; set; }

    public string? Error { get; set; }

    public int RetryCount { get; set; }

    /// <summary>
    /// Composed dedup key for events implementing
    /// <see cref="Resrcify.SharedKernel.Abstractions.DomainDrivenDesign.IDedupable"/>.
    /// NULL when the source event is not dedupable. Format:
    /// <c>{event.GetType().FullName}:{IDedupable.DedupKey}</c>.
    /// </summary>
    public string? DedupKey { get; set; }
}