using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Resrcify.SharedKernel.WebApiExample.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Data Source=LocalDatabase.db";
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
             .UseSqlite(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
