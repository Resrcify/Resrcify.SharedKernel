using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Resrcify.SharedKernel.UnitOfWork.Primitives;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

[SuppressMessage(
    "Maintainability",
    "CA1515:Because an application's API isn't typically referenced from outside the assembly, types can be made internal",
    Justification = "Needed to be public due to being abstract and test classes needs to be public.")]
public abstract class DbSetupBase : IAsyncLifetime, IDisposable
{
    internal TestDbContext DbContext { get; }
    internal UnitOfWork<TestDbContext> UnitOfWork { get; }
    private bool _disposed;
    protected DbSetupBase(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("Data Source=:memory:;Cache=Shared");

        if (interceptors is not null)
        {
            builder.AddInterceptors(interceptors);
        }

        DbContext = new TestDbContext(builder.Options);

        UnitOfWork = new UnitOfWork<TestDbContext>(DbContext);
    }


    public async Task InitializeAsync()
    {
        await DbContext.Database.OpenConnectionAsync();
        await DbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_disposed)
        {
            await DbContext.Database.CloseConnectionAsync();
            await DbContext.DisposeAsync();
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            UnitOfWork.Dispose();
            // Note: DbContext should only be disposed here if DisposeAsync hasn't already done it
            DbContext?.Dispose();
        }

        _disposed = true;
    }
}