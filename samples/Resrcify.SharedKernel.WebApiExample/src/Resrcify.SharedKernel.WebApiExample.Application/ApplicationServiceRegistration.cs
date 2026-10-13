using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Mediator.Extensions;

namespace Resrcify.SharedKernel.WebApiExample.Application;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediator(config =>
        {
            config.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly());
            // Logging -> Validation -> Transaction -> UnitOfWork -> Caching (the first is the outermost).
            config.AddStandardBehaviors();
        });

        services.AddValidatorsFromAssembly(
            Assembly.GetExecutingAssembly(),
            includeInternalTypes: true);

        return services;
    }
}