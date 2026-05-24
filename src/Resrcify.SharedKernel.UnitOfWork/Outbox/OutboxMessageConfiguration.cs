using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// EF Core mapping for <see cref="OutboxMessage"/>, including the index that backs
/// the outbox polling query (<c>WHERE ProcessedOnUtc IS NULL ORDER BY OccurredOnUtc</c>).
/// </summary>
public sealed class OutboxMessageConfiguration
    : IEntityTypeConfiguration<OutboxMessage>
{
    private const int DedupKeyMaxLength = 512;

    private readonly string _tableName;
    private readonly string? _schema;
    private readonly Action<IndexBuilder<OutboxMessage>>? _configureUnprocessedIndex;
    private readonly Action<IndexBuilder<OutboxMessage>>? _configureDedupIndex;

    /// <param name="tableName">Table name for the outbox messages.</param>
    /// <param name="schema">
    /// Database schema for the table. When <see langword="null"/> the provider's
    /// default schema is used (e.g. <c>public</c> on PostgreSQL, <c>dbo</c> on SQL
    /// Server). The schema is created automatically by EF migrations / EnsureCreated.
    /// </param>
    /// <param name="configureUnprocessedIndex">
    /// Optional hook to apply provider-specific tuning to the unprocessed-messages
    /// index. For PostgreSQL, combine the prebuilt
    /// <see cref="PostgresOutboxIndexes.PartialUnprocessed"/> filter with your own
    /// <c>IncludeProperties(...)</c> call:
    /// <code>
    /// configureUnprocessedIndex: index =>
    /// {
    ///     index.IncludeProperties("Type", "Content");
    ///     PostgresOutboxIndexes.Instance.PartialUnprocessed(index);
    /// }
    /// </code>
    /// </param>
    /// <param name="configureDedupIndex">
    /// Optional hook to apply provider-specific tuning to the dedup index. Defaults
    /// to a non-unique single-column index on <c>DedupKey</c> that backs the
    /// outbox writer's pre-check query. To upgrade to a strict partial unique
    /// index on PostgreSQL (pairs with
    /// <see cref="PostgresOnConflictOutboxInsertStrategy"/>):
    /// <code>
    /// configureDedupIndex: PostgresOutboxIndexes.Instance.PartialUniqueDedup
    /// </code>
    /// Without that strategy in place, a unique-constraint violation inside the
    /// SaveChanges pipeline will fail the entire user transaction.
    /// </param>
    public OutboxMessageConfiguration(
        string tableName = "OutboxMessages",
        string? schema = null,
        Action<IndexBuilder<OutboxMessage>>? configureUnprocessedIndex = null,
        Action<IndexBuilder<OutboxMessage>>? configureDedupIndex = null)
    {
        _tableName = tableName;
        _schema = schema;
        _configureUnprocessedIndex = configureUnprocessedIndex;
        _configureDedupIndex = configureDedupIndex;
    }

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(_tableName, _schema);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.DedupKey).HasMaxLength(DedupKeyMaxLength);

        // Composite index supports the seek (ProcessedOnUtc IS NULL) and the
        // ordered scan (ORDER BY OccurredOnUtc) of the polling query on every
        // relational provider. Covering columns / partial filters are added per
        // provider via the optional hook.
        var unprocessedIndex = builder
            .HasIndex(x => new { x.ProcessedOnUtc, x.OccurredOnUtc })
            .HasDatabaseName("IX_OutboxMessages_Unprocessed");

        _configureUnprocessedIndex?.Invoke(unprocessedIndex);

        // Backs the outbox writer's pre-check: "is there already a pending row
        // for this dedup key?". Single-column DedupKey index so it matches the
        // ON CONFLICT ("DedupKey") inference clause used by
        // PostgresOnConflictOutboxInsertStrategy. Non-unique by default — apps
        // that need strict race-proofing can promote it via the optional hook.
        var dedupIndex = builder
            .HasIndex(x => x.DedupKey)
            .HasDatabaseName("IX_OutboxMessages_Dedup");

        _configureDedupIndex?.Invoke(dedupIndex);
    }
}