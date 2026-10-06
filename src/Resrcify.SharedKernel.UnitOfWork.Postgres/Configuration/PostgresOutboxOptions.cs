using Resrcify.SharedKernel.UnitOfWork.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;

/// <summary>The outbox's write side, set with <see cref="PostgresDbContextBuilder.WithOutbox"/>.</summary>
public sealed class PostgresOutboxOptions
{
    /// <summary>
    /// The serializer events are written with; registered as the <c>IOutboxSerializer</c> the outbox job reads with.
    /// <see langword="null"/> uses the registered one, or <c>SystemTextJsonOutboxSerializer</c> when none is.
    /// </summary>
    public IOutboxSerializer? Serializer { get; set; }

    /// <summary>
    /// Inserts with <c>INSERT ... ON CONFLICT ("DedupKey") ... DO NOTHING</c> (<c>PostgresOnConflictOutboxInsertStrategy</c>,
    /// what the services use today), so a duplicate deduplicable event is skipped by the database instead of failing
    /// the save. Needs the partial unique index (<c>PostgresOutboxIndexes.PartialUniqueDedup</c>). Off by default: the
    /// messages are inserted with the rest of the save.
    /// </summary>
    public bool OnConflictDoNothing { get; set; }
}
