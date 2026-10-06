using System;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Web.Exceptions;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class ResultProblemDetailsServiceCollectionExtensions
{
    /// <summary>
    /// Answers unhandled exceptions with the same problem details as a failed result
    /// (<see cref="ResultExceptionHandler"/>): registers the handler and <c>AddProblemDetails()</c>. The host still
    /// calls <c>app.UseExceptionHandler()</c>, early in the pipeline.
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
        services.AddExceptionHandler<ResultExceptionHandler>();
        return services;
    }
}
