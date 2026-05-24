namespace Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

/// <summary>
/// Marks a domain event as deduplicable in the outbox. When two events of the same
/// runtime type with the same <see cref="DedupKey"/> would land in the outbox while
/// the first is still unprocessed, the second is silently coalesced (no row written).
/// <para>
/// The outbox writer composes the storage key as
/// <c>{event.GetType().FullName}:{DedupKey}</c> so different event types with the
/// same business key never collide.
/// </para>
/// <para>
/// Dedup is best-effort: the writer performs a pre-check inside the SaveChanges
/// pipeline, which catches the common case but is not race-proof under concurrent
/// transactions from different <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// instances. If strict uniqueness is required, add a partial unique index on the
/// outbox table at the application level
/// (PostgreSQL: <c>CREATE UNIQUE INDEX ON "OutboxMessages" ("DedupKey") WHERE
/// "ProcessedOnUtc" IS NULL AND "DedupKey" IS NOT NULL;</c>) and handle the
/// resulting <c>DbUpdateException</c> at the call site.
/// </para>
/// </summary>
public interface IDedupable
{
    /// <summary>
    /// Stable key identifying the "subject" of this event for dedup purposes.
    /// Typically the aggregate id (e.g. shard id). The writer prefixes it with the
    /// event's full type name to scope dedup per-type.
    /// </summary>
    string DedupKey { get; }
}
