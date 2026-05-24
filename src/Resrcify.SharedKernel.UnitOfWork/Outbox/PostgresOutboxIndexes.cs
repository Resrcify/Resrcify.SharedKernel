using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// PostgreSQL <see cref="IOutboxIndexes"/> implementation. Pairs with
/// <see cref="PostgresOnConflictOutboxInsertStrategy"/>: the filter SQL exposed
/// here is exactly what the strategy's <c>ON CONFLICT</c> clause infers against.
/// <para>
/// Covering <c>INCLUDE</c> columns live in the provider-specific Npgsql package
/// (<c>Npgsql.EntityFrameworkCore.PostgreSQL</c>) so they're not surfaced here.
/// Chain <c>.IncludeProperties(...)</c> in your own configurator and apply the
/// helper alongside it — see example below.
/// </para>
/// <example>
/// <code>
/// modelBuilder.ApplyOutboxMessageConfiguration(
///     configureUnprocessedIndex: index =>
///     {
///         index.IncludeProperties("Type", "Content");
///         PostgresOutboxIndexes.Instance.PartialUnprocessed(index);
///     },
///     configureDedupIndex: PostgresOutboxIndexes.Instance.PartialUniqueDedup);
/// </code>
/// </example>
/// </summary>
public sealed class PostgresOutboxIndexes : IOutboxIndexes
{
    /// <summary>
    /// Shared singleton — the delegate properties are immutable, so a single
    /// instance is reused without re-allocating closures on every access.
    /// Also convenient for DI registration:
    /// <c>services.AddSingleton&lt;IOutboxIndexes&gt;(PostgresOutboxIndexes.Instance)</c>.
    /// </summary>
    public static PostgresOutboxIndexes Instance { get; } = new();

    /// <summary>
    /// Raw filter SQL applied by <see cref="PartialUnprocessed"/>. Exposed as a
    /// constant so callers composing their own index can reuse the exact filter
    /// the rest of the helpers target.
    /// </summary>
    public const string UnprocessedFilterSql = "\"ProcessedOnUtc\" IS NULL";

    /// <summary>
    /// Raw filter SQL applied by <see cref="PartialUniqueDedup"/>. Targets only
    /// unprocessed rows with a non-null <c>DedupKey</c> — the scope
    /// <see cref="PostgresOnConflictOutboxInsertStrategy"/> conflicts against.
    /// </summary>
    public const string PartialDedupFilterSql = "\"DedupKey\" IS NOT NULL AND \"ProcessedOnUtc\" IS NULL";

    public Action<IndexBuilder<OutboxMessage>> PartialUnprocessed { get; } =
        index => index.HasFilter(UnprocessedFilterSql);

    public Action<IndexBuilder<OutboxMessage>> PartialUniqueDedup { get; } =
        index => index
            .IsUnique()
            .HasFilter(PartialDedupFilterSql);
}
