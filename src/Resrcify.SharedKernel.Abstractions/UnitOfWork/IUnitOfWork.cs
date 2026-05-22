using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.UnitOfWork;

public interface IUnitOfWork : IDisposable
{
    Task CompleteAsync(
        CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        TimeSpan? commandLifetime = null,
        CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(
        CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a transaction: the operation, the
    /// resulting <see cref="CompleteAsync"/>, and the commit either all succeed or
    /// the whole transaction is rolled back. The transaction is always disposed.
    /// </summary>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        TimeSpan? commandTimeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a transaction and commits only when
    /// it returns a successful <see cref="Result"/>. A failure result rolls the
    /// transaction back (without throwing) and is returned to the caller; an
    /// exception also rolls back and is rethrown. The transaction is always disposed.
    /// </summary>
    Task<TResponse> ExecuteInTransactionAsync<TResponse>(
        Func<CancellationToken, Task<TResponse>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        TimeSpan? commandTimeout = null,
        CancellationToken cancellationToken = default)
        where TResponse : Result;
}