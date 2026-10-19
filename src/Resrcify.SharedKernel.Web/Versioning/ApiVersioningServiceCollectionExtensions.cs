using System;
using Asp.Versioning;
using Asp.Versioning.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace Resrcify.SharedKernel.Web.Versioning;

/// <summary>The API versioning and versioned OpenAPI documents every service sets up.</summary>
public static class ApiVersioningServiceCollectionExtensions
{
    /// <summary>
    /// API versioning as every service has it, with an OpenAPI document per version (<c>v1</c>): versions reported, a
    /// request without a version taken as 1.0, the explorer grouping by <c>'v'VVV</c> and substituting the version in
    /// the URL. Map the documents with <c>app.MapOpenApi().WithDocumentPerVersion()</c> and build the version set with
    /// <c>app.CreateApiVersionSet()</c> (<see cref="ApiVersioning"/>).
    /// </summary>
    /// <param name="services">The service's container.</param>
    /// <param name="versionReader">
    /// Where a request names its version. <c>null</c> (the default) keeps Asp.Versioning's own (the
    /// <c>api-version</c> query string or a URL segment); a service that reads it from the media type passes
    /// <c>new MediaTypeApiVersionReader("api-version")</c>. Keep the reader a service has: it is wire behaviour.
    /// </param>
    /// <param name="configureOpenApi">Configures the versioned documents (e.g. a schema transformer), if given.</param>
    /// <returns>The versioning builder, for more of Asp.Versioning's setup.</returns>
    public static IApiVersioningBuilder AddServiceApiVersioning(
        this IServiceCollection services,
        IApiVersionReader? versionReader = null,
        Action<VersionedOpenApiOptions>? configureOpenApi = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var versioning = services
            .AddApiVersioning(options =>
            {
                options.ReportApiVersions = true;
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.DefaultApiVersion = new ApiVersion(1, 0);
                if (versionReader is not null)
                    options.ApiVersionReader = versionReader;
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        return configureOpenApi is null
            ? versioning.AddOpenApi()
            : versioning.AddOpenApi(configureOpenApi);
    }
}
