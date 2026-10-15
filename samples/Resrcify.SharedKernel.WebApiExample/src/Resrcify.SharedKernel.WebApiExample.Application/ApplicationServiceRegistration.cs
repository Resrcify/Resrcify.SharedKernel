using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Mediator.Extensions;

namespace Resrcify.SharedKernel.WebApiExample.Application;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // The handlers and the FluentValidation validators of this assembly (internal ones included), and the
        // behaviors: Logging -> Idempotency -> Validation -> Transaction -> UnitOfWork -> Caching (the first outermost).
        services.AddMediator(config => config
            .RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly())
            .AddStandardBehaviors());

        return services;
    }
}
