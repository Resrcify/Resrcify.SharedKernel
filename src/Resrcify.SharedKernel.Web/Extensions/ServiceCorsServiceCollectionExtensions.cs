using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class ServiceCorsServiceCollectionExtensions
{
    /// <summary>
    /// The CORS policy every service names and applies with <c>app.UseCors(policyName)</c>: any origin, header and
    /// method, without credentials.
    /// </summary>
    /// <remarks>
    /// CORS is not the authorization boundary here: every non-anonymous route is guarded by a bearer token a browser
    /// doesn't attach on its own, and <c>AllowAnyOrigin</c> excludes <c>AllowCredentials</c>, so no cookie or credentialed
    /// cross-site request can ride this policy. Restricting the origins is worth doing once the first-party front-ends
    /// are settled; until then a service that needs it registers a policy of its own.
    /// </remarks>
    /// <param name="services">The service's container.</param>
    /// <param name="policyName">The policy's name, as <c>UseCors</c> names it (e.g. <c>ShardCors</c>).</param>
    [SuppressMessage(
        "Security",
        "S5122:Make sure this permissive CORS policy is safe here",
        Justification = "No credentials ride it (AllowAnyOrigin excludes AllowCredentials) and bearer tokens guard every non-anonymous route; see remarks.")]
    public static IServiceCollection AddServiceCors(
        this IServiceCollection services,
        string policyName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);

        return services.AddCors(options =>
            options.AddPolicy(policyName, policy => policy
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod()));
    }
}
