using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Web.Extensions;

namespace Resrcify.SharedKernel.WebApiExample.Presentation;

public static class PresentationServiceRegistration
{
    public static IServiceCollection AddPresentationServices(this IServiceCollection services)
    {
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        });

        // Problem details for failed results, unhandled exceptions and requests that can't be bound.
        services.AddResultProblemDetails();
        // Every IEndpoint of this layer, mapped by app.MapEndpoints().
        services.AddEndpoints(typeof(PresentationServiceRegistration).Assembly);
        services.AddOpenApi();
        return services;
    }
}
