using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Resrcify.SharedKernel.Caching.Abstractions;
using Resrcify.SharedKernel.Caching.Primitives;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.WebApiExample.Persistence;

namespace Resrcify.SharedKernel.WebApiExample.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddDistributedMemoryCache();
        services.AddSingleton<ICachingService, DistributedCachingService>();
        services.AddSwaggerGen(options =>
            options.CustomSchemaIds(type => type.ToString()));
        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
        services.ConfigureOptions<ProcessOutboxMessagesJobSetup<AppDbContext>>();
        return services;
    }
}