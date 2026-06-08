using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// PostgreSQL-specific insert strategy that uses
/// <c>INSERT ... ON CONFLICT ("DedupKey") WHERE "ProcessedOnUtc" IS NULL AND
/// "DedupKey" IS NOT NULL DO NOTHING</c>. The conflict is resolved silently by
/// the database, so no <see cref="DbUpdateException"/> bubbles up and the user
/// transaction continues unaffected.
/// <para>
/// Opt-in: register this strategy in DI alongside the matching partial unique
/// index. Use the prebuilt
/// <see cref="PostgresOutboxIndexes.PartialUniqueDedup"/> helper on
/// <see cref="OutboxMessageConfiguration"/>'s <c>configureDedupIndex</c> hook:
/// <code>
/// configureDedupIndex: PostgresOutboxIndexes.PartialUniqueDedup
/// </code>
/// </para>
/// </summary>
public sealed class PostgresOnConflictOutboxInsertStrategy : IOutboxInsertStrategy
{
    public async Task InsertAsync(
        DbContext context,
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
            return;

        var qualifiedTable = ResolveQualifiedTableName(context);

        // Two SQL paths: passing a null DedupKey through FormattableString boxes to
        // object and loses the static type EF/Npgsql needs to map the parameter.
        // Non-dedupable rows take a plain INSERT (DedupKey defaults to NULL at the
        // column level) and skip ON CONFLICT, which has nothing to conflict on.
        var dedupSqlFormat = string.Format(
            CultureInfo.InvariantCulture,
            """
            INSERT INTO {0} ("Id", "Type", "Content", "OccurredOnUtc", "ProcessedOnUtc", "Error", "RetryCount", "DedupKey")
            VALUES ({{0}}, {{1}}, {{2}}, {{3}}, NULL, NULL, 0, {{4}})
            ON CONFLICT ("DedupKey") WHERE "ProcessedOnUtc" IS NULL AND "DedupKey" IS NOT NULL
            DO NOTHING;
            """,
            qualifiedTable);

        var plainSqlFormat = string.Format(
            CultureInfo.InvariantCulture,
            """
            INSERT INTO {0} ("Id", "Type", "Content", "OccurredOnUtc", "ProcessedOnUtc", "Error", "RetryCount")
            VALUES ({{0}}, {{1}}, {{2}}, {{3}}, NULL, NULL, 0);
            """,
            qualifiedTable);

        foreach (var message in messages)
        {
            FormattableString sql = message.DedupKey is null
                ? FormattableStringFactory.Create(
                    plainSqlFormat,
                    message.Id,
                    message.Type,
                    message.Content,
                    message.OccurredOnUtc)
                : FormattableStringFactory.Create(
                    dedupSqlFormat,
                    message.Id,
                    message.Type,
                    message.Content,
                    message.OccurredOnUtc,
                    message.DedupKey);

            await context.Database
                .ExecuteSqlAsync(sql, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string ResolveQualifiedTableName(DbContext context)
    {
        var entityType = context.Model.FindEntityType(typeof(OutboxMessage))
            ?? throw new InvalidOperationException(
                $"{nameof(OutboxMessage)} is not configured in the DbContext model.");

        var tableName = entityType.GetTableName() ?? "OutboxMessages";
        var schema = entityType.GetSchema();

        return string.IsNullOrEmpty(schema)
            ? $"\"{tableName}\""
            : $"\"{schema}\".\"{tableName}\"";
    }
}
