using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.GenericUnitOfWork.Abstractions;

namespace Resrcify.SharedKernel.GenericUnitOfWork.Primitives;

public sealed class UnitOfWork<TDbContext> : IUnitOfWork
    where TDbContext : DbContext
{

    private readonly TDbContext _context;

    public UnitOfWork(TDbContext context)
        => _context = context;

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, TimeSpan? commandLifetime = null, CancellationToken cancellationToken = default)
    {
        if (commandLifetime is not null)
            _context.Database.SetCommandTimeout((int)commandLifetime.Value.TotalSeconds);

        await _context.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        var currentTransaction = _context.Database.CurrentTransaction;
        if (currentTransaction == null)
            return;

        await currentTransaction.CommitAsync(cancellationToken);
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        var currentTransaction = _context.Database.CurrentTransaction;
        if (currentTransaction == null)
            return;

        await currentTransaction.RollbackAsync(cancellationToken);
    }
}