using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// PostgreSQL claim: locks the message row with <c>FOR UPDATE SKIP LOCKED</c> for the rest of the
/// processing transaction. Another instance skips a locked row instead of waiting for it. The table and column names
/// come from the EF model, so a naming convention (e.g. snake_case) or <c>HasColumnName</c> is followed.
/// </summary>
public sealed class PostgresOutboxLaneClaim : IOutboxLaneClaim
{
    // The claim's SQL per model: built once, from names that don't change while the model lives.
    private static readonly ConditionalWeakTable<IModel, string> ClaimSql = [];

    public static PostgresOutboxLaneClaim Instance { get; } = new();

    public async Task<bool> TryClaimAsync(DbContext context, Guid messageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sql = ClaimSql.GetValue(context.Model, BuildClaimSql);

#pragma warning disable EF1002 // The names come from the EF model, not from input; the id is a parameter.
        var claimed = await context.Database
            .SqlQueryRaw<Guid>(sql, messageId)
            .ToListAsync(cancellationToken);
#pragma warning restore EF1002
        return claimed.Count == 1;
    }

    private static string BuildClaimSql(IModel model)
    {
        var entity = model.FindEntityType(typeof(OutboxMessage))
            ?? throw new InvalidOperationException("OutboxMessage is not mapped; call ApplyOutboxMessageConfiguration.");
        var tableName = entity.GetTableName()
            ?? throw new InvalidOperationException("OutboxMessage is not mapped to a table.");
        var schema = entity.GetSchema();
        var store = StoreObjectIdentifier.Table(tableName, schema);
        var table = schema is null ? Quote(tableName) : $"{Quote(schema)}.{Quote(tableName)}";
        var id = Quote(ColumnName(entity, nameof(OutboxMessage.Id), store));
        var processedOnUtc = Quote(ColumnName(entity, nameof(OutboxMessage.ProcessedOnUtc), store));

        return $"SELECT {id} AS \"Value\" FROM {table} WHERE {id} = {{0}} AND {processedOnUtc} IS NULL FOR UPDATE SKIP LOCKED";
    }

    private static string ColumnName(IEntityType entity, string property, StoreObjectIdentifier store)
        => entity.FindProperty(property)?.GetColumnName(store)
            ?? throw new InvalidOperationException($"OutboxMessage.{property} is not mapped to a column.");

    private static string Quote(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
