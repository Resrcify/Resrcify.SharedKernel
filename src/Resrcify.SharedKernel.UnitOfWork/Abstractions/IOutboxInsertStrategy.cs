using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Abstractions;

/// <summary>
/// Strategy for persisting outbox messages produced by the
/// <see cref="Interceptors.InsertOutboxMessagesInterceptor"/>. The default
/// implementation uses EF Core's change tracker
/// (<see cref="DbSet{TEntity}.AddRangeAsync(IEnumerable{TEntity}, CancellationToken)"/>);
/// alternative implementations exist for providers that can swallow
/// unique-constraint violations natively (e.g. Postgres
/// <c>ON CONFLICT DO NOTHING</c>).
/// </summary>
public interface IOutboxInsertStrategy
{
    /// <summary>
    /// Inserts the supplied outbox messages in the current SaveChanges flow.
    /// Implementations participate in the same transaction as the surrounding
    /// SaveChanges call; the messages must be visible once the user's transaction
    /// commits.
    /// </summary>
    Task InsertAsync(
        DbContext context,
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether <see cref="InsertAsync"/> writes to the database itself (raw SQL), rather than adding the messages to
    /// the change tracker for EF's save to write. The outbox interceptor then wraps the save in a transaction of its
    /// own (when there is none), so the messages and the save commit together. Defaults to <see langword="true"/>.
    /// </summary>
    bool InsertsOutsideSaveChanges => true;
}
