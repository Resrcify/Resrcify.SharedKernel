using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.Primitives;

/// <summary>
/// <see cref="IUnitOfWork"/> over <typeparamref name="TDbContext"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>ExecuteInTransactionAsync</c> runs under the context's execution strategy, so it works with a retrying one
/// (<c>EnableRetryOnFailure</c>): a transient failure runs the whole operation again, on a cleared change tracker. That
/// is only safe when the context tracked nothing before the call (the operation's entities are then all its own): when
/// it did, a transient failure is not retried but thrown, as an <see cref="InvalidOperationException"/> holding it.</item>
/// <item>Called while a transaction is already open (e.g. a transactional command sent from a domain event handler
/// that the outbox runs in its transaction), it joins that transaction on a savepoint: pending work is saved first, a
/// success saves the operation's work, and a failure (or an exception) undoes just the operation's work, in the
/// database and in the change tracker. It can't be stricter than the open transaction: asking for a stricter isolation
/// level throws.</item>
/// <item><c>CompleteAsync</c> (and <c>TryCompleteAsync</c>) outside a transaction saves through the execution strategy
/// too, so a save works with a retrying one. A transaction begun with <c>BeginTransactionAsync</c> doesn't: EF Core
/// refuses user-initiated transactions under a retrying strategy; use <c>ExecuteInTransactionAsync</c>.</item>
/// <item>A command timeout applies to that call only, and is rounded up to whole seconds; <see cref="TimeSpan.Zero"/>
/// or <see cref="Timeout.InfiniteTimeSpan"/> means no timeout.</item>
/// <item>The DbContext belongs to the DI scope, which disposes it; disposing the unit of work leaves it alone.</item>
/// </list>
/// </remarks>
public sealed class UnitOfWork<TDbContext> : IUnitOfWork
    where TDbContext : DbContext
{
    private readonly TDbContext _context;

    public UnitOfWork(TDbContext context)
        => _context = context;

    public async Task CompleteAsync(
        CancellationToken cancellationToken = default)
    {
        // Inside a transaction, its owner runs the execution strategy (ExecuteInTransactionAsync does).
        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        // Saved through the execution strategy, so a save that opens a transaction of its own (the outbox interceptor
        // does, with an insert strategy that writes outside SaveChanges) works under a retrying one
        // (EnableRetryOnFailure), and a transient failure saves again. Without retries this just saves.
        await _context.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                _context,
                static (context, token) => context.SaveChangesAsync(token),
                cancellationToken);
    }

    public async Task<Result> TryCompleteAsync(
        CancellationToken cancellationToken = default)
    {
        var inTransaction = _context.Database.CurrentTransaction is not null;
        try
        {
            await CompleteAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception exception) when (PersistenceErrors.TryGetError(exception, inTransaction, out var error))
        {
            // The database refused the whole save, so nothing pending was saved. Kept tracked, it would go out again
            // with the scope's next save (and be refused again, or saved by work that has nothing to do with it).
            DiscardPendingChanges();
            return Result.Failure(error);
        }
    }

    public async Task<TResponse> ExecuteAsync<TResponse>(
        Func<CancellationToken, Task<TResponse>> operation,
        CancellationToken cancellationToken = default)
        where TResponse : Result
    {
        ArgumentNullException.ThrowIfNull(operation);
        var before = TrackedState.Capture(_context);
        try
        {
            var response = await operation(cancellationToken);
            if (!response.IsSuccess)
                before.UndoChangesSince(_context);
            return response;
        }
        catch
        {
            before.UndoChangesSince(_context);
            throw;
        }
    }

    public void Dispose()
        => GC.SuppressFinalize(this);

    public async Task BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        TimeSpan? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        if (commandTimeout is not null)
            _context.Database.SetCommandTimeout(ToSeconds(commandTimeout.Value));

        await _context.Database.BeginTransactionAsync(
            isolationLevel,
            cancellationToken);
    }

    public async Task CommitTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        var currentTransaction = _context.Database.CurrentTransaction;
        if (currentTransaction == null)
            return;

        await currentTransaction.CommitAsync(
            cancellationToken);
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        var currentTransaction = _context.Database.CurrentTransaction;
        if (currentTransaction == null)
            return;

        await currentTransaction.RollbackAsync(
            cancellationToken);
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        TimeSpan? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await ExecuteInTransactionAsync(
            async token =>
            {
                await operation(token);
                return Result.Success();
            },
            isolationLevel,
            commandTimeout,
            cancellationToken);
    }

    public async Task<TResponse> ExecuteInTransactionAsync<TResponse>(
        Func<CancellationToken, Task<TResponse>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        TimeSpan? commandTimeout = null,
        CancellationToken cancellationToken = default)
        where TResponse : Result
    {
        ArgumentNullException.ThrowIfNull(operation);
        var previousTimeout = _context.Database.GetCommandTimeout();
        if (commandTimeout is not null)
            _context.Database.SetCommandTimeout(ToSeconds(commandTimeout.Value));
        try
        {
            if (_context.Database.CurrentTransaction is { } outer)
            {
                EnsureIsolationIsKept(outer, isolationLevel);
                return await ExecuteOnSavepointAsync(outer, operation, cancellationToken);
            }

            return await ExecuteRetryingAsync(operation, isolationLevel, cancellationToken);
        }
        finally
        {
            if (commandTimeout is not null)
                _context.Database.SetCommandTimeout(previousTimeout);
        }
    }

    // Under a retrying strategy a transient failure runs the operation again on a cleared change tracker. Entities the
    // context tracked before the call would be cleared too, though the caller still holds and changes them: the retry
    // would then save without them and report success. So with anything tracked before, the failure isn't retried.
    private async Task<TResponse> ExecuteRetryingAsync<TResponse>(
        Func<CancellationToken, Task<TResponse>> operation,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
        where TResponse : Result
    {
        var trackedBefore = _context.ChangeTracker.Entries().Any();
        var attempt = 0;
        Exception? lastFailure = null;
        return await _context.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                async token =>
                {
                    if (attempt++ > 0)
                    {
                        if (trackedBefore)
                            throw new InvalidOperationException(
                                "The transaction failed transiently and can't be retried: the DbContext tracked " +
                                "entities before it began, which a retry would lose. Load them inside the operation " +
                                "(or send the command from a fresh scope) so it can be retried.",
                                lastFailure);
                        // A retry starts over: what the failed attempt tracked is stale.
                        _context.ChangeTracker.Clear();
                    }

                    try
                    {
                        return await ExecuteInNewTransactionAsync(operation, isolationLevel, token);
                    }
                    catch (Exception exception)
                    {
                        lastFailure = exception;
                        throw;
                    }
                },
                cancellationToken);
    }

    private async Task<TResponse> ExecuteInNewTransactionAsync<TResponse>(
        Func<CancellationToken, Task<TResponse>> operation,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
        where TResponse : Result
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            isolationLevel,
            cancellationToken);
        try
        {
            var response = await operation(cancellationToken);

            // Commit only on a successful result; a failure rolls back the work
            // the operation did and is handed back to the caller as-is.
            if (response.IsSuccess)
            {
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return response;
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    // The failure that got here is what the caller (and a retrying strategy) must see. A rollback can fail too, when the
    // transaction is already gone (a dropped connection, a failed commit); disposing it then is all there is to do.
    private static async Task RollbackQuietlyAsync(
        IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackFailure) when (rollbackFailure is not OutOfMemoryException)
        {
            // Dropped on purpose: the original failure is rethrown by the caller.
        }
    }

    // A savepoint runs at the open transaction's level: a stricter one (e.g. Serializable inside ReadCommitted) can't be
    // had, and running without it would silently lose the guarantee the operation asked for.
    private static void EnsureIsolationIsKept(
        IDbContextTransaction outer,
        IsolationLevel requested)
    {
        var current = outer.GetDbTransaction().IsolationLevel;
        if (Strictness(current) > 0 && Strictness(requested) > Strictness(current))
            throw new InvalidOperationException(
                $"The operation asks for {requested} isolation, but runs inside a transaction already open at " +
                $"{current}, which it can only join. Send it outside that transaction (e.g. from a fresh scope), or " +
                "open the outer transaction at that level.");
    }

    private static int Strictness(
        IsolationLevel level)
        => level switch
        {
            IsolationLevel.ReadUncommitted => 1,
            IsolationLevel.ReadCommitted => 2,
            IsolationLevel.RepeatableRead => 3,
            IsolationLevel.Snapshot => 4,
            IsolationLevel.Serializable => 5,
            _ => 0,
        };

    /// <summary>
    /// Runs inside the transaction already open, on a savepoint: the work pending before is saved first (it belongs
    /// to the outer transaction), so a failure can undo exactly the operation's work, in the database and in the
    /// change tracker. The outer transaction's owner commits.
    /// </summary>
    private async Task<TResponse> ExecuteOnSavepointAsync<TResponse>(
        IDbContextTransaction outer,
        Func<CancellationToken, Task<TResponse>> operation,
        CancellationToken cancellationToken)
        where TResponse : Result
    {
        await _context.SaveChangesAsync(cancellationToken);
        var savepoint = $"uow_{Guid.NewGuid():N}";
        await outer.CreateSavepointAsync(savepoint, cancellationToken);
        var saved = new SavedInside(_context.ChangeTracker);
        try
        {
            var response = await operation(cancellationToken);
            if (response.IsSuccess)
            {
                await _context.SaveChangesAsync(cancellationToken);
                await outer.ReleaseSavepointAsync(savepoint, cancellationToken);
            }
            else
            {
                await outer.RollbackToSavepointAsync(savepoint, cancellationToken);
                await UndoSavepointAsync(saved, cancellationToken);
            }
            return response;
        }
        catch
        {
            try
            {
                await outer.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                await UndoSavepointAsync(saved, CancellationToken.None);
            }
            catch (Exception undoFailure) when (undoFailure is not OutOfMemoryException)
            {
                // The transaction is broken (its owner rolls it all back): the original failure is what matters.
                DiscardPendingChanges();
            }
            throw;
        }
        finally
        {
            saved.Dispose();
        }
    }

    // After a rollback to the savepoint: the pending changes are discarded, and what the operation saved inside it
    // (now undone in the database) is put back as the database has it: inserted rows untracked, updated or deleted rows
    // read again.
    private async Task UndoSavepointAsync(
        SavedInside saved,
        CancellationToken cancellationToken)
    {
        var written = saved.Entries;
        saved.Dispose();
        DiscardPendingChanges();
        foreach (var (entity, savedAs) in written)
        {
            var entry = _context.Entry(entity);
            if (savedAs == EntityState.Added)
            {
                entry.State = EntityState.Detached;
                continue;
            }

            if (entry.State == EntityState.Detached)
                entry.State = EntityState.Unchanged;
            await entry.ReloadAsync(cancellationToken);
        }
    }

    /// <summary>Puts every tracked entity back as it was saved, and drops the domain events raised since.</summary>
    private void DiscardPendingChanges()
    {
        foreach (var entry in _context.ChangeTracker.Entries().ToList())
        {
            Revert(entry);
            if (entry.Entity is IAggregateRoot aggregate)
                aggregate.ClearDomainEvents();
        }
    }

    private static void Revert(
        EntityEntry entry)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                entry.State = EntityState.Detached;
                break;
            case EntityState.Modified:
            case EntityState.Deleted:
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
                break;
            default:
                break;
        }
    }

    private static int ToSeconds(TimeSpan timeout)
    {
        // 0 is no timeout, as Npgsql and SqlClient read it.
        if (timeout == TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            return 0;

        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        return (int)Math.Min(int.MaxValue, Math.Ceiling(timeout.TotalSeconds));
    }

    /// <summary>
    /// What the change tracker held before an operation: which entities had changes pending and each aggregate's domain
    /// events, so that what the operation alone changed can be undone. Changes pending before are left as they are.
    /// </summary>
    private sealed class TrackedState
    {
        private readonly HashSet<object> _pending;
        private readonly Dictionary<IAggregateRoot, IReadOnlyList<IDomainEvent>> _events;

        private TrackedState(
            HashSet<object> pending,
            Dictionary<IAggregateRoot, IReadOnlyList<IDomainEvent>> events)
        {
            _pending = pending;
            _events = events;
        }

        public static TrackedState Capture(
            DbContext context)
        {
            var pending = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var events = new Dictionary<IAggregateRoot, IReadOnlyList<IDomainEvent>>(ReferenceEqualityComparer.Instance);
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Unchanged)
                    pending.Add(entry.Entity);
                if (entry.Entity is IAggregateRoot aggregate)
                    events[aggregate] = aggregate.GetDomainEvents();
            }
            return new TrackedState(pending, events);
        }

        // An entity pending before keeps its changes: the operation's own changes to it can't be told apart from them.
        public void UndoChangesSince(
            DbContext context)
        {
            foreach (var entry in context.ChangeTracker.Entries().ToList())
            {
                if (!_pending.Contains(entry.Entity))
                    Revert(entry);
                if (entry.Entity is IAggregateRoot aggregate)
                    DropEventsRaisedSince(aggregate);
            }
        }

        private void DropEventsRaisedSince(
            IAggregateRoot aggregate)
        {
            if (!_events.TryGetValue(aggregate, out var before))
            {
                aggregate.ClearDomainEvents();
                return;
            }

            var raisedBefore = new HashSet<IDomainEvent>(before, ReferenceEqualityComparer.Instance);
            foreach (var raised in aggregate.GetDomainEvents().Where(raised => !raisedBefore.Contains(raised)))
                aggregate.RemoveDomainEvent(raised);
        }
    }

    /// <summary>
    /// Records the entities a savepoint's saves wrote (inserted, updated or deleted), from the change tracker's state
    /// changes as each save accepts its changes.
    /// </summary>
    private sealed class SavedInside : IDisposable
    {
        private readonly ChangeTracker _changeTracker;
        private readonly Dictionary<object, EntityState> _entries = new(ReferenceEqualityComparer.Instance);

        public SavedInside(
            ChangeTracker changeTracker)
        {
            _changeTracker = changeTracker;
            _changeTracker.StateChanged += OnStateChanged;
        }

        /// <summary>Each entity written, with the state it was saved in (first one wins: an insert stays an insert).</summary>
        public List<KeyValuePair<object, EntityState>> Entries => [.. _entries];

        public void Dispose()
            => _changeTracker.StateChanged -= OnStateChanged;

        private void OnStateChanged(
            object? sender,
            EntityStateChangedEventArgs args)
        {
            var saved = args.OldState is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && args.NewState is EntityState.Unchanged or EntityState.Detached
                && !(args.OldState == EntityState.Added && args.NewState == EntityState.Detached);
            if (saved)
                _entries.TryAdd(args.Entry.Entity, args.OldState);
        }
    }
}
