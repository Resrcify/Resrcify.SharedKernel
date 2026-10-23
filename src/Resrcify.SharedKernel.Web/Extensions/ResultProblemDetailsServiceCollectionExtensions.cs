using System;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Web.Exceptions;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class ResultProblemDetailsServiceCollectionExtensions
{
    /// <summary>
    /// Answers unhandled exceptions with the same problem details as a failed result
    /// (<see cref="ResultExceptionHandler"/>): registers the handler and <c>AddProblemDetails()</c>. The host still
    /// calls <c>app.UseExceptionHandler()</c>, early in the pipeline, or Observability's <c>app.UseServiceDefaults()</c>,
    /// which adds it after the request logging. A minimal-API parameter that fails to bind is
    /// answered with 400 problem details naming it, in every environment: it turns on
    /// <see cref="RouteHandlerOptions.ThrowOnBadRequest"/> unless
    /// <see cref="ResultProblemDetailsOptions.ProblemDetailsForBindingFailures"/> is <see langword="false"/>. An exception
    /// the <see cref="ITransientFailureClassifier"/> calls transient is answered 503 with a <c>Retry-After</c>: it
    /// registers the default classifier unless one is registered already.
    /// </summary>
    public static IServiceCollection AddResultProblemDetails(
        this IServiceCollection services,
        Action<ResultProblemDetailsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<ResultProblemDetailsOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.AddProblemDetails();
        services.TryAddTransientFailureClassifier();
        services.AddExceptionHandler<ResultExceptionHandler>();

        // After every Configure: routing's own turns it on in Development only, whenever routing was registered.
        services.AddOptions<RouteHandlerOptions>()
            .PostConfigure<IOptions<ResultProblemDetailsOptions>>(static (routeHandlers, problemDetails) =>
            {
                if (problemDetails.Value.ProblemDetailsForBindingFailures)
                    routeHandlers.ThrowOnBadRequest = true;
            });
        return services;
    }
}
