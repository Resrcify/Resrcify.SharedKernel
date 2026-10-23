using System;
using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Routing;

namespace Resrcify.SharedKernel.Web.Versioning;

/// <summary>
/// The service's API version set: version 1.0, reported in the <c>api-supported-versions</c> header. Each endpoint uses
/// it with <c>.WithApiVersionSet(ApiVersioning.VersionSet).HasApiVersion(new ApiVersion(1, 0))</c>.
/// </summary>
/// <remarks>
/// The set is complete when it is created and an endpoint never changes it, so every host in a process can share it.
/// Asp.Versioning's own set records each version an endpoint declares in a collection that isn't safe to write from two
/// threads: shared by hosts building their endpoints at the same time (tests' hosts, side by side), it was corrupted and
/// a host failed to start ("Operations that change non-concurrent collections must have exclusive access", 4.10.0 and
/// before).
/// </remarks>
public static class ApiVersioning
{
    /// <summary>The version set: version 1.0, versions reported. Complete, and never changed by an endpoint.</summary>
    public static ApiVersionSet VersionSet { get; } = new CompleteApiVersionSetBuilder(new ApiVersion(1, 0)).Build();

    /// <summary>
    /// Nothing left to build: <see cref="VersionSet"/> is complete from the start. Kept so a <c>Program</c> that calls it
    /// (<c>app.CreateApiVersionSet()</c>, before the endpoints are mapped) needn't change.
    /// </summary>
    public static IEndpointRouteBuilder CreateApiVersionSet(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app;
    }
}
