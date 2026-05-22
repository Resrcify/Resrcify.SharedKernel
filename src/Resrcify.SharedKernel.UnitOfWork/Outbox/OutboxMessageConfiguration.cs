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
    private readonly string _tableName;
    private readonly string? _schema;
    private readonly Action<IndexBuilder<OutboxMessage>>? _configureUnprocessedIndex;

    /// <param name="tableName">Table name for the outbox messages.</param>
    /// <param name="schema">
    /// Database schema for the table. When <see langword="null"/> the provider's
    /// default schema is used (e.g. <c>public</c> on PostgreSQL, <c>dbo</c> on SQL
    /// Server). The schema is created automatically by EF migrations / EnsureCreated.
    /// </param>
    /// <param name="configureUnprocessedIndex">
    /// Optional hook to apply provider-specific tuning to the unprocessed-messages index.
    /// Use it to add a covering <c>INCLUDE</c> or a partial filter on providers that
    /// support them, e.g. PostgreSQL:
    /// <code>
    /// configureUnprocessedIndex: index => index
    ///     .IncludeProperties(m => new { m.Type, m.Content })
    ///     .HasFilter("\"ProcessedOnUtc\" IS NULL");
    /// </code>
    /// </param>
    public OutboxMessageConfiguration(
        string tableName = "OutboxMessages",
        string? schema = null,
        Action<IndexBuilder<OutboxMessage>>? configureUnprocessedIndex = null)
    {
        _tableName = tableName;
        _schema = schema;
        _configureUnprocessedIndex = configureUnprocessedIndex;
    }

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(_tableName, _schema);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).IsRequired();
        builder.Property(x => x.Content).IsRequired();

        // Composite index supports the seek (ProcessedOnUtc IS NULL) and the
        // ordered scan (ORDER BY OccurredOnUtc) of the polling query on every
        // relational provider. Covering columns / partial filters are added per
        // provider via the optional hook.
        var index = builder
            .HasIndex(x => new { x.ProcessedOnUtc, x.OccurredOnUtc })
            .HasDatabaseName("IX_OutboxMessages_Unprocessed");

        _configureUnprocessedIndex?.Invoke(index);
    }
}