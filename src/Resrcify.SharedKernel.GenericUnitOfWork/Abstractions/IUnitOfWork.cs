using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.GenericUnitOfWork.Abstractions;

public interface IUnitOfWork : IDisposable
{
    Task CompleteAsync(CancellationToken cancellationToken = default);
    Task BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, TimeSpan? commandLifetime = null, CancellationToken cancellationToken = default);
    Task CommitTransaction(CancellationToken cancellationToken = default);
    Task RollbackTransaction(CancellationToken cancellationToken = default);
}
