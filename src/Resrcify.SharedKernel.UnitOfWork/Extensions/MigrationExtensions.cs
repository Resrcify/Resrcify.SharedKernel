using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

public static class MigrationsExtensions
{
    public static void ApplyMigrations<T>(this IServiceCollection services) where T : DbContext
    {
        using IServiceScope scope = services.BuildServiceProvider().CreateScope();

        using T? dbContext = scope.ServiceProvider.GetService<T>();
        if (dbContext is null)
            return;
        dbContext.Database.Migrate();
    }
}