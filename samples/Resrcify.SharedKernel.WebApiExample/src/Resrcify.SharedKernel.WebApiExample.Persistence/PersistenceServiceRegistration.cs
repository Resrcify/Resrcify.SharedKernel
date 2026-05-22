using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
using Resrcify.SharedKernel.WebApiExample.Persistence.Repositories;

namespace Resrcify.SharedKernel.WebApiExample.Persistence;

public static class PersistenceServiceRegistration
{
    public static IServiceCollection AddPersistanceServices(this IServiceCollection services, IConfiguration configuration)
    {
        // One outbox serializer instance, shared by the write side (interceptor)
        // and the read side (processing job) so they cannot drift apart.
        var outboxSerializer = new SystemTextJsonOutboxSerializer();

        services.AddDbContext<AppDbContext>(option =>
        {
            option.UseNpgsql(configuration.GetConnectionString("Database"));
            option.AddInterceptors(
                new InsertOutboxMessagesInterceptor(outboxSerializer),
                new UpdateAuditableEntitiesInterceptor(),
                new UpdateDeletableEntitiesInterceptor());
        });

        // Read side: unit of work, the serializer for the job, and the Quartz job.
        services.AddOutboxProcessing<AppDbContext>(outboxSerializer);

        services.AddScoped<ICompanyRepository, CompanyRepository>();

        services.ApplyMigrations<AppDbContext>();

        return services;
    }
}
