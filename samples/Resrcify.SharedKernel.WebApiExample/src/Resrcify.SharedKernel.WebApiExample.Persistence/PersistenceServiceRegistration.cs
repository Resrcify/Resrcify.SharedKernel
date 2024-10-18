using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Primitives;

namespace Resrcify.SharedKernel.WebApiExample.Persistence;

public static class PersistenceServiceRegistration
{
    public static IServiceCollection AddPersistanceServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(option =>
        {
            option.UseSqlite(configuration["Database"]);
            option.AddInterceptors(
                new InsertOutboxMessagesInterceptor(),
                new UpdateAuditableEntitiesInterceptor(),
                new UpdateDeletableEntitiesInterceptor());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork<AppDbContext>>();
        services.ApplyMigrations<AppDbContext>();

        return services;
    }
}