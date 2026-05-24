using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>
/// Convenience helpers for registering the outbox mapping on a <see cref="ModelBuilder"/>.
/// </summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Applies <see cref="OutboxMessageConfiguration"/>, including the index used by
    /// the outbox processing job. Call this from <c>OnModelCreating</c>.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="tableName">Table name for the outbox messages.</param>
    /// <param name="schema">
    /// Database schema for the table. When <see langword="null"/> the provider's
    /// default schema is used (e.g. <c>public</c> on PostgreSQL).
    /// </param>
    /// <param name="configureUnprocessedIndex">
    /// Optional hook for provider-specific index tuning (covering columns, partial filter).
    /// </param>
    /// <param name="configureDedupIndex">
    /// Optional hook for tuning the dedup index. Defaults to a non-unique
    /// composite on <c>(DedupKey, ProcessedOnUtc)</c>. Promote to a strict
    /// partial UNIQUE on PostgreSQL via
    /// <see cref="Outbox.PostgresOutboxIndexes.PartialUniqueDedup"/> when wiring
    /// <see cref="Outbox.PostgresOnConflictOutboxInsertStrategy"/>.
    /// </param>
    public static ModelBuilder ApplyOutboxMessageConfiguration(
        this ModelBuilder modelBuilder,
        string tableName = "OutboxMessages",
        string? schema = null,
        Action<IndexBuilder<OutboxMessage>>? configureUnprocessedIndex = null,
        Action<IndexBuilder<OutboxMessage>>? configureDedupIndex = null)
    {
        modelBuilder.ApplyConfiguration(
            new OutboxMessageConfiguration(
                tableName,
                schema,
                configureUnprocessedIndex,
                configureDedupIndex));

        return modelBuilder;
    }
}