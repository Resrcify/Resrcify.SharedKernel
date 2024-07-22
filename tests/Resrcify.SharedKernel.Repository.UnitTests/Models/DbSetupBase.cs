using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Resrcify.SharedKernel.Repository.UnitTests.Models;

public abstract class DbSetupBase : IAsyncLifetime
{
    protected TestDbContext DbContext;

    public DbSetupBase()
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("Data Source=:memory:;Cache=Shared");

        DbContext = new TestDbContext(builder.Options);
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