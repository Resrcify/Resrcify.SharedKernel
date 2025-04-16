using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Resrcify.SharedKernel.Repository.UnitTests.Models;
[SuppressMessage(
    "Maintainability",
    "CA1515:Because an application's API isn't typically referenced from outside the assembly, types can be made internal",
    Justification = "Needed to be public due to being abstract and test classes needs to be public.")]
public abstract class DbSetupBase : IAsyncLifetime, IDisposable
{
    internal TestDbContext DbContext { get; }
    private bool _disposed;
    protected DbSetupBase()
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("Data Source=:memory:;Cache=Shared");

        DbContext = new TestDbContext(builder.Options);
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
            DbContext?.Dispose();
        }

        _disposed = true;
    }
}