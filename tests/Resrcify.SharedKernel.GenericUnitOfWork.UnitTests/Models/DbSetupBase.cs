using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Resrcify.SharedKernel.GenericUnitOfWork.Primitives;
using Xunit;

namespace Resrcify.SharedKernel.GenericUnitOfWork.UnitTests.Models;

public abstract class DbSetupBase : IAsyncLifetime
{
    protected TestDbContext DbContext;
    protected UnitOfWork<TestDbContext> UnitOfWork;

    public DbSetupBase(params IInterceptor[] interceptors)
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


    public async Task DisposeAsync()
    {
        await DbContext.Database.CloseConnectionAsync();
        await DbContext.DisposeAsync();
    }

    public async Task InitializeAsync()
    {
        await DbContext.Database.OpenConnectionAsync();
        await DbContext.Database.EnsureCreatedAsync();
    }
}