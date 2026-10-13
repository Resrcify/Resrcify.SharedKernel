using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.Web.Health;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class HealthEndpointExtensions
{
    /// <summary>
    /// Maps the health endpoints every service has, open to anonymous callers (the probes have no token), each
    /// answering with <see cref="HealthResponseWriter"/>'s JSON (200 when Healthy or Degraded, 503 when Unhealthy):
    /// <list type="bullet">
    /// <item><c>/health</c>: every check;</item>
    /// <item><c>/health/ready</c>: the checks tagged <see cref="HealthTags.Ready"/> (the readiness probe);</item>
    /// <item><c>/health/live</c>: the checks tagged <see cref="HealthTags.Live"/> (the liveness probe; Healthy
    /// without any).</item>
    /// </list>
    /// The checks are registered as usual, with <c>services.AddHealthChecks()</c>.
    /// </summary>
    /// <returns>The three endpoints, to add conventions to all of them.</returns>
    public static IEndpointConventionBuilder MapHealthEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<HealthEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new HealthEndpointOptions();
        configure?.Invoke(options);

        var builders = new EndpointConventionBuilders(
        [
            endpoints.MapHealthChecks(options.Path, Checks(_ => true)),
            endpoints.MapHealthChecks(options.ReadyPath, Checks(check => check.Tags.Contains(HealthTags.Ready))),
            endpoints.MapHealthChecks(options.LivePath, Checks(check => check.Tags.Contains(HealthTags.Live))),
        ]);
        builders.AllowAnonymous();
        return builders;
    }

    private static HealthCheckOptions Checks(
        Func<HealthCheckRegistration, bool> predicate)
        => new()
        {
            Predicate = predicate,
            ResponseWriter = HealthResponseWriter.WriteAsync,
        };
}
