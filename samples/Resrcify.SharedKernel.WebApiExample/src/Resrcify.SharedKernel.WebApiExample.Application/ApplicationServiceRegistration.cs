using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Messaging.Behaviors;

namespace Resrcify.SharedKernel.WebApiExample.Application;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly());
            config.AddOpenBehavior(typeof(LoggingPipelineBehavior<,>));
            config.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
            config.AddOpenBehavior(typeof(TransactionPipelineBehavior<,>));
            config.AddOpenBehavior(typeof(UnitOfWorkPipelineBehavior<,>));
            config.AddOpenBehavior(typeof(CachingPipelineBehavior<,>));
        });

        services.AddValidatorsFromAssembly(
            Assembly.GetExecutingAssembly(),
            includeInternalTypes: true);

        return services;
    }
}