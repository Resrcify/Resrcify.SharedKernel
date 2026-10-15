using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
using Resrcify.SharedKernel.WebApiExample.Persistence.Repositories;

namespace Resrcify.SharedKernel.WebApiExample.Persistence;

public static class PersistenceServiceRegistration
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // The context from the Database section (validated on start), with the auditable, soft-delete and outbox
        // interceptors built by the container, the outbox woken on commit (NOTIFY) and claimed per message
        // (PostgresOutboxLaneClaim, the default with WithOutbox()) so several instances never publish one twice.
        services.AddPostgresDbContext<AppDbContext>(configuration, db => db
            .WithOutbox()
            .WithOutboxWakeUp());

        // The unit of work and the outbox processing job (and its lanes), every 20 s on top of the wake-up.
        services.AddOutboxProcessing<AppDbContext>(outbox => outbox.ProcessInterval = TimeSpan.FromSeconds(20));

        // Migrates while the host starts, before it serves (Migrations:Run).
        services.AddMigrationsOnStartup<AppDbContext>(configuration);

        services.AddScoped<ICompanyRepository, CompanyRepository>();

        services.AddHealthChecks()
            .AddPostgres<AppDbContext>()   // tagged ready
            .AddOutbox<AppDbContext>();    // tagged ready, Degraded on a backlog

        return services;
    }
}
