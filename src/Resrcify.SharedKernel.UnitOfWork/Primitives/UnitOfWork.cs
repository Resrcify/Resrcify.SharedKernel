using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
/// (<c>EnableRetryOnFailure</c>): a transient failure runs the whole operation again, on a cleared change tracker.</item>
/// <item>Called while a transaction is already open (e.g. a transactional command sent from a domain event handler
/// that the outbox runs in its transaction), it joins that transaction on a savepoint: pending work is saved first, a
/// success saves the operation's work, and a failure (or an exception) undoes just the operation's work.</item>
/// <item><c>CompleteAsync</c> (and <c>TryCompleteAsync</c>) outside a transaction saves through the execution strategy
/// too, so a save works with a retrying one. A transaction begun with <c>BeginTransactionAsync</c> doesn't: EF Core
/// refuses user-initiated transactions under a retrying strategy; use <c>ExecuteInTransactionAsync</c>.</item>
/// <item>A command timeout applies to that call only, and is rounded up to whole seconds (0 would mean no timeout).</item>
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
            return Result.Failure(error);
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
                return await ExecuteOnSavepointAsync(outer, operation, cancellationToken);

            var attempt = 0;
            return await _context.Database
                .CreateExecutionStrategy()
                .ExecuteAsync(
                    async token =>
                    {
                        // A retry starts over: what the failed attempt tracked is stale.
                        if (attempt++ > 0)
                            _context.ChangeTracker.Clear();
                        return await ExecuteInNewTransactionAsync(operation, isolationLevel, token);
                    },
                    cancellationToken);
        }
        finally
        {
            if (commandTimeout is not null)
                _context.Database.SetCommandTimeout(previousTimeout);
        }
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
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

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
                DiscardPendingChanges();
            }
            return response;
        }
        catch
        {
            await outer.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            DiscardPendingChanges();
            throw;
        }
    }

    /// <summary>Puts every tracked entity back as it was saved, and drops the domain events raised since.</summary>
    private void DiscardPendingChanges()
    {
        foreach (var entry in _context.ChangeTracker.Entries().ToList())
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
            if (entry.Entity is IAggregateRoot aggregate)
                aggregate.ClearDomainEvents();
        }
    }

    private static int ToSeconds(TimeSpan timeout)
        => Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
}
