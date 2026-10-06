using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Abstractions;

/// <summary>
/// Told by the <see cref="Interceptors.InsertOutboxMessagesInterceptor"/> when a save wrote outbox messages, e.g. to
/// wake the outbox's processing instead of waiting for its next poll (<c>WithOutboxWakeUp</c> on PostgreSQL).
/// </summary>
/// <remarks>
/// It is called once the save succeeded, on the save's connection: inside the caller's transaction when there is one
/// (so whatever it writes commits or rolls back with the save), otherwise after the save committed.
/// </remarks>
public interface IOutboxSaveObserver
{
    /// <param name="context">The context that saved.</param>
    /// <param name="messages">The outbox messages the save wrote (with an insert that skips duplicates, those it tried to).</param>
    /// <param name="cancellationToken">The save's cancellation token.</param>
    Task MessagesSavedAsync(
        DbContext context,
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken);
}
