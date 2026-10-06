using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Interceptors;

/// <summary>
/// Turns the domain events of the aggregates being saved into outbox messages, in the same save (and transaction).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A transaction of its own is started only when there are messages and the insert strategy writes them outside
/// EF's save (<see cref="IOutboxInsertStrategy.InsertsOutsideSaveChanges"/>); the default strategy doesn't, so a plain
/// save also works under a retrying execution strategy.</item>
/// <item>Events are cleared from their aggregates only once the save has succeeded. A failed save keeps them, rolls back
/// its own transaction and stops tracking the messages it added, so a retried save writes each event once.</item>
/// <item>Saving domain events needs <c>SaveChangesAsync</c>: the synchronous <c>SaveChanges</c> throws rather than drop them.</item>
/// <item>Once a save wrote messages, the <paramref name="observers"/> are told (see <see cref="IOutboxSaveObserver"/>).</item>
/// <item>One instance serves every context and save (it keeps no state of its own), so it can be a singleton: built by
/// the container (<c>AddOutboxInterceptor</c>), it takes the registered serializer, insert strategy, clock and observers.</item>
/// </list>
/// </remarks>
public sealed class InsertOutboxMessagesInterceptor(
    IOutboxSerializer serializer,
    IOutboxInsertStrategy? insertStrategy = null,
    TimeProvider? timeProvider = null,
    IEnumerable<IOutboxSaveObserver>? observers = null)
    : SaveChangesInterceptor
{
    private readonly IOutboxInsertStrategy _insertStrategy = insertStrategy ?? new DefaultOutboxInsertStrategy();
    private readonly IOutboxSaveObserver[] _observers = observers is null ? [] : [.. observers];

    private static readonly ConditionalWeakTable<DbContext, SaveInProgress> _saves = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null && AggregatesWithEvents(eventData, eventData.Context).Count > 0)
            throw new NotSupportedException(
                "Domain events are written to the outbox by SaveChangesAsync; the synchronous SaveChanges would drop them.");
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            await PrepareAsync(eventData, context, cancellationToken).ConfigureAwait(false);
        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && _saves.TryGetValue(context, out var save))
        {
            _saves.Remove(context);
            foreach (var aggregate in save.Aggregates)
                aggregate.ClearDomainEvents();
            if (save.Transaction is { } transaction)
            {
                try
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
            }

            await NotifyObserversAsync(context, save.Messages, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyObserversAsync(
        DbContext context,
        List<OutboxMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
            return;

        foreach (var observer in _observers)
            await observer.MessagesSavedAsync(context, messages, cancellationToken).ConfigureAwait(false);
    }

    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && _saves.TryGetValue(context, out var save))
        {
            _saves.Remove(context);
            await UndoAsync(context, save).ConfigureAwait(false);
        }

        await base.SaveChangesFailedAsync(eventData, cancellationToken).ConfigureAwait(false);
    }

    private async Task PrepareAsync(
        DbContextEventData saving,
        DbContext context,
        CancellationToken cancellationToken)
    {
        var aggregates = AggregatesWithEvents(saving, context);
        if (aggregates.Count == 0)
            return;

        var save = new SaveInProgress(aggregates);
        try
        {
            var messages = await ToOutboxMessagesAsync(context, aggregates, cancellationToken).ConfigureAwait(false);
            if (messages.Count == 0)
            {
                _saves.AddOrUpdate(context, save);
                return;
            }

            if (_insertStrategy.InsertsOutsideSaveChanges && context.Database.CurrentTransaction is null)
                save.Transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            save.Messages = messages;
            await _insertStrategy.InsertAsync(context, messages, cancellationToken).ConfigureAwait(false);
            _saves.AddOrUpdate(context, save);
        }
        catch
        {
            // EF doesn't call SaveChangesFailed for an interceptor's own exception: undo here.
            await UndoAsync(context, save).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Rolls back this save's own transaction, and stops tracking the messages it added; the events stay.</summary>
    private static async Task UndoAsync(DbContext context, SaveInProgress save)
    {
        foreach (var message in save.Messages)
        {
            var entry = context.Entry(message);
            if (entry.State != EntityState.Detached)
                entry.State = EntityState.Detached;
        }

        if (save.Transaction is not { } transaction)
            return;
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception rollbackFailure) when (rollbackFailure is InvalidOperationException or System.Data.Common.DbException)
        {
            // Swallowed: surfacing a rollback error would mask the original failure.
        }
        finally
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static List<IAggregateRoot> AggregatesWithEvents(
        DbContextEventData saving,
        DbContext context)
        => [.. SaveChangesEntries
            .Where<IAggregateRoot>(
                saving,
                context,
                entry => entry.Entity.GetDomainEvents().Count > 0)
            .Select(entry => entry.Entity)];

    private async Task<List<OutboxMessage>> ToOutboxMessagesAsync(
        DbContext context,
        List<IAggregateRoot> aggregates,
        CancellationToken cancellationToken)
    {
        var outboxMessages = new List<OutboxMessage>();
        var dedupKeysSeen = new HashSet<string>(StringComparer.Ordinal);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

        foreach (var domainEvent in aggregates.SelectMany(aggregate => aggregate.GetDomainEvents()))
        {
            string? dedupKey = null;
            if (domainEvent is IDedupable dedupable)
            {
                dedupKey = $"{domainEvent.GetType().FullName}:{dedupable.DedupKey}";

                if (!dedupKeysSeen.Add(dedupKey))
                    continue;
            }

            outboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                OccurredOnUtc = now,
                Type = domainEvent.GetType().FullName!,
                Content = serializer.Serialize(domainEvent),
                DedupKey = dedupKey
            });
        }

        // Race-tolerant, not race-proof: concurrent transactions from different DbContexts
        // can each pass this check and both insert. Pair with PostgresOnConflictOutboxInsertStrategy
        // + a partial UNIQUE index for strict uniqueness.
        if (dedupKeysSeen.Count == 0)
            return outboxMessages;

        var keysToCheck = dedupKeysSeen.ToList();
        var existingKeys = await context
            .Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null
                && m.DedupKey != null
                && keysToCheck.Contains(m.DedupKey))
            .Select(m => m.DedupKey!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existingKeys.Count == 0)
            return outboxMessages;

        var existingKeySet = new HashSet<string>(existingKeys, StringComparer.Ordinal);
        return [.. outboxMessages.Where(m => m.DedupKey is null || !existingKeySet.Contains(m.DedupKey))];
    }

    /// <summary>What one save of a context did, so it can be finished or undone.</summary>
    private sealed class SaveInProgress(List<IAggregateRoot> aggregates)
    {
        public List<IAggregateRoot> Aggregates { get; } = aggregates;

        public List<OutboxMessage> Messages { get; set; } = [];

        public IDbContextTransaction? Transaction { get; set; }
    }
}
