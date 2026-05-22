using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Caching.Primitives;

namespace Resrcify.SharedKernel.WebApiExample.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddDistributedMemoryCache();
        services.AddSingleton<ICachingService, DistributedCachingService>();
        services.AddSwaggerGen(options =>
            options.CustomSchemaIds(type => type.ToString()));

        // Quartz hosts the outbox job; the job itself is registered by
        // AddOutboxProcessing in the persistence layer.
        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
        return services;
    }
}