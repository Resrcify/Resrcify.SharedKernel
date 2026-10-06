using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// PostgreSQL claim: locks the message row with <c>FOR UPDATE SKIP LOCKED</c> for the rest of the
/// processing transaction. Another instance skips a locked row instead of waiting for it.
/// </summary>
public sealed class PostgresOutboxLaneClaim : IOutboxLaneClaim
{
    public static PostgresOutboxLaneClaim Instance { get; } = new();

    public async Task<bool> TryClaimAsync(DbContext context, Guid messageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var entity = context.Model.FindEntityType(typeof(OutboxMessage))
            ?? throw new InvalidOperationException("OutboxMessage is not mapped; call ApplyOutboxMessageConfiguration.");
        var table = entity.GetSchema() is { } schema
            ? $"\"{schema}\".\"{entity.GetTableName()}\""
            : $"\"{entity.GetTableName()}\"";

#pragma warning disable EF1002 // The table name comes from the EF model, not from input; the id is a parameter.
        var claimed = await context.Database
            .SqlQueryRaw<Guid>(
                $"SELECT \"Id\" AS \"Value\" FROM {table} WHERE \"Id\" = {{0}} AND \"ProcessedOnUtc\" IS NULL FOR UPDATE SKIP LOCKED",
                messageId)
            .ToListAsync(cancellationToken);
#pragma warning restore EF1002
        return claimed.Count == 1;
    }
}
