using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Resrcify.SharedKernel.WebApiExample.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = "host=webapiexampledb;database=AppDb;username=ExampleUser;password=testingStuffOut;";
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
             .UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
