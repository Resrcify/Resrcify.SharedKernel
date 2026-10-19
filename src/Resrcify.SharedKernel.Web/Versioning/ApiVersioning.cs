using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Resrcify.SharedKernel.Web.Versioning;

/// <summary>
/// The service's API version set: version 1.0, reported in the <c>api-supported-versions</c> header. Built once in
/// <c>Program</c> (<c>app.CreateApiVersionSet()</c>, before the endpoints are mapped); each endpoint uses it with
/// <c>.WithApiVersionSet(ApiVersioning.VersionSet).HasApiVersion(new ApiVersion(1, 0))</c>.
/// </summary>
public static class ApiVersioning
{
    /// <summary>The version set <see cref="CreateApiVersionSet"/> built; null before it ran.</summary>
    public static ApiVersionSet VersionSet { get; private set; } = null!;

    /// <summary>Builds <see cref="VersionSet"/>: version 1.0, versions reported.</summary>
    public static IEndpointRouteBuilder CreateApiVersionSet(this IEndpointRouteBuilder app)
    {
        VersionSet = app
            .NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .ReportApiVersions()
            .Build();

        return app;
    }
}
