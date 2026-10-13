using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
/// save also works under a retrying execution strategy. Inside a transaction that is already open, such a strategy's
/// rows are written after a savepoint, so a failed save takes them back.</item>
/// <item>Events are cleared from their aggregates only once the save has committed. A failed save keeps them, rolls back
/// what it wrote and stops tracking the messages it added, so a retried save writes each event once; that includes a
/// failure EF doesn't report to interceptors (a concurrency conflict, a cancellation), undone when the context next
/// saves. When this save's own transaction fails to commit, the change tracker is put back as it was before the save
/// (EF had already accepted its changes), so a retrying execution strategy writes it all again.</item>
/// <item>The events of one save are given increasing <see cref="OutboxMessage.OccurredOnUtc"/> times (a microsecond
/// apart, in the order raised), so the outbox, which reads in that order, publishes them in that order.</item>
/// <item>Saving domain events needs <c>SaveChangesAsync</c>: the synchronous <c>SaveChanges</c> throws rather than drop them.</item>
/// <item>Once a save wrote messages, the <paramref name="observers"/> are told (see <see cref="IOutboxSaveObserver"/>).</item>
/// <item>One instance serves every context and save (it keeps no state of its own), so it can be a singleton: built by
/// the container (<c>AddOutboxInterceptor</c>), it takes the registered serializer, insert strategy, clock and observers.</item>
/// </list>
/// </remarks>
public sealed partial class InsertOutboxMessagesInterceptor(
    IOutboxSerializer serializer,
    IOutboxInsertStrategy? insertStrategy = null,
    TimeProvider? timeProvider = null,
    IEnumerable<IOutboxSaveObserver>? observers = null,
    ILogger<InsertOutboxMessagesInterceptor>? logger = null)
    : SaveChangesInterceptor
{
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    private static readonly TimeSpan BetweenEvents = TimeSpan.FromMicroseconds(1);

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
        {
            await UndoUnfinishedSaveAsync(context).ConfigureAwait(false);
            await PrepareAsync(eventData, context, cancellationToken).ConfigureAwait(false);
        }
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
            await CommitAsync(context, save, cancellationToken).ConfigureAwait(false);
            foreach (var aggregate in save.Aggregates)
                aggregate.ClearDomainEvents();

            await NotifyObserversAsync(context, save.Messages, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            await UndoUnfinishedSaveAsync(context).ConfigureAwait(false);

        await base.SaveChangesFailedAsync(eventData, cancellationToken).ConfigureAwait(false);
    }

    public override async Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            await UndoUnfinishedSaveAsync(context).ConfigureAwait(false);

        await base.SaveChangesCanceledAsync(eventData, cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyObserversAsync(
        DbContext context,
        List<OutboxMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
            return;

        // Inside the caller's transaction nothing is committed yet: an observer's failure fails the save, and the
        // transaction rolls back with it. Outside one the save has committed: failing it now would have the caller try
        // again what is already saved, so the failure is logged instead.
        var committed = context.Database.CurrentTransaction is null;
        foreach (var observer in _observers)
        {
            if (!committed)
            {
                await observer.MessagesSavedAsync(context, messages, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await observer.MessagesSavedAsync(context, messages, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogObserverFailed(exception, observer.GetType().Name, context.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox save observer {Observer} failed after a {DbContext} save committed; the save stands")]
    private partial void LogObserverFailed(Exception exception, string observer, string dbContext);

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

            if (_insertStrategy.InsertsOutsideSaveChanges)
                await GuardOutsideWritesAsync(saving, context, save, cancellationToken).ConfigureAwait(false);

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

    // Rows the strategy writes itself must go when the save fails. Outside a transaction they are written in one of
    // this save's own (and the change tracker is noted, to put back should its commit fail); inside one, after a
    // savepoint, since EF's own savepoint is taken after them and the caller's transaction would otherwise keep them.
    private static async Task GuardOutsideWritesAsync(
        DbContextEventData saving,
        DbContext context,
        SaveInProgress save,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not { } outer)
        {
            save.Pending = PendingEntry.Capture(saving, context);
            save.Transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!outer.SupportsSavepoints)
            return;

        save.Outer = outer;
        save.Savepoint = $"outbox_{Guid.NewGuid():N}";
        await outer.CreateSavepointAsync(save.Savepoint, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CommitAsync(
        DbContext context,
        SaveInProgress save,
        CancellationToken cancellationToken)
    {
        if (save.Outer is { } outer && save.Savepoint is { } savepoint)
        {
            await outer.ReleaseSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (save.Transaction is not { } transaction)
            return;

        try
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // EF accepted the save's changes before this commit: as they are, a retried save would write nothing and
            // report success. Put them back, so it writes them (and, the events kept, their messages) again.
            PendingEntry.Restore(context, save.Pending);
            throw;
        }
        finally
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    // A save whose failure EF didn't report (a concurrency conflict, a cancellation) left its messages tracked and its
    // transaction or savepoint open: undone before the context saves again, so nothing of it is written twice.
    private static async Task UndoUnfinishedSaveAsync(
        DbContext context)
    {
        if (!_saves.TryGetValue(context, out var unfinished))
            return;

        _saves.Remove(context);
        await UndoAsync(context, unfinished).ConfigureAwait(false);
    }

    /// <summary>Rolls back what this save wrote itself, and stops tracking the messages it added; the events stay.</summary>
    private static async Task UndoAsync(DbContext context, SaveInProgress save)
    {
        foreach (var message in save.Messages)
        {
            var entry = context.Entry(message);
            if (entry.State != EntityState.Detached)
                entry.State = EntityState.Detached;
        }

        try
        {
            if (save.Outer is { } outer && save.Savepoint is { } savepoint)
                await outer.RollbackToSavepointAsync(savepoint, CancellationToken.None).ConfigureAwait(false);
            else if (save.Transaction is { } transaction)
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception rollbackFailure) when (rollbackFailure is InvalidOperationException or System.Data.Common.DbException)
        {
            // Swallowed: surfacing a rollback error would mask the original failure.
        }
        finally
        {
            if (save.Transaction is { } transaction)
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
                // A microsecond apart (the precision PostgreSQL keeps), in the order raised: the outbox reads by
                // OccurredOnUtc, and the ids are random, so equal times would publish in no particular order.
                OccurredOnUtc = now + BetweenEvents * outboxMessages.Count,
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

        /// <summary>This save's own transaction, begun when the strategy writes outside the save and none was open.</summary>
        public IDbContextTransaction? Transaction { get; set; }

        /// <summary>The transaction already open, and the savepoint taken in it before the strategy wrote.</summary>
        public IDbContextTransaction? Outer { get; set; }

        public string? Savepoint { get; set; }

        /// <summary>The entries the save writes, as they were before it, for a commit of its own that fails.</summary>
        public List<PendingEntry> Pending { get; set; } = [];
    }

    /// <summary>An entry a save writes, as it was before: its state, original values and modified properties.</summary>
    private sealed record PendingEntry(
        object Entity,
        EntityState State,
        PropertyValues OriginalValues,
        string[] ModifiedProperties)
    {
        public static List<PendingEntry> Capture(
            DbContextEventData saving,
            DbContext context)
            => [.. SaveChangesEntries
                .Where<object>(
                    saving,
                    context,
                    entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => new PendingEntry(
                    entry.Entity,
                    entry.State,
                    entry.OriginalValues.Clone(),
                    [.. entry.Properties.Where(property => property.IsModified).Select(property => property.Metadata.Name)]))];

        // Back as before the save: added again, modified again (with the values read, so a concurrency token still
        // matches the row, which the rollback left as it was), deleted again.
        public static void Restore(
            DbContext context,
            List<PendingEntry> pending)
        {
            foreach (var before in pending)
            {
                var entry = context.Entry(before.Entity);
                if (before.State == EntityState.Added)
                {
                    entry.State = EntityState.Added;
                    continue;
                }

                entry.State = EntityState.Unchanged;
                entry.OriginalValues.SetValues(before.OriginalValues);
                if (before.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Deleted;
                    continue;
                }

                foreach (var property in before.ModifiedProperties)
                    entry.Property(property).IsModified = true;
            }
        }
    }
}
