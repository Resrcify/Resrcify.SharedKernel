using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Caching.Abstractions;
using Resrcify.SharedKernel.Caching.Primitives;

namespace Resrcify.SharedKernel.WebApiExample.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddDistributedMemoryCache();
        services.AddSingleton<ICachingService, InMemoryCachingService>();
        services.AddSwaggerGen(options =>
            options.CustomSchemaIds(type => type.ToString()));

        return services;
    }
}