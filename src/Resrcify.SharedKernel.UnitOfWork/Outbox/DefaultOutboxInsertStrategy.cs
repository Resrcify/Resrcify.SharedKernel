using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// EF Core change-tracker insert. Pairs with the writer's pre-check dedup —
/// race-tolerant, but a partial unique index on the table would fail this
/// strategy with <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>
/// and rollback the user transaction. Use
/// <see cref="PostgresOnConflictOutboxInsertStrategy"/> instead if a strict
/// unique index is required.
/// </summary>
public sealed class DefaultOutboxInsertStrategy : IOutboxInsertStrategy
{
    public Task InsertAsync(
        DbContext context,
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken)
        => context.Set<OutboxMessage>().AddRangeAsync(messages, cancellationToken);

    /// <summary>The messages are tracked, so EF's own save (and its transaction) writes them with everything else.</summary>
    public bool InsertsOutsideSaveChanges => false;
}
