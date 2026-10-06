using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Resrcify.SharedKernel.Observability.Extensions;

public static class ServiceTelemetryEndpointRouteBuilderExtensions
{
    /// <summary>The path Prometheus scrapes by default.</summary>
    public const string DefaultMetricsPath = "/metrics";

    /// <summary>
    /// Maps the Prometheus scrape endpoint of <c>AddServiceTelemetry</c>'s metrics at <paramref name="path"/>,
    /// open to anonymous callers (Prometheus has no token), whatever the service's authorization fallback policy.
    /// </summary>
    public static IEndpointConventionBuilder MapServiceMetrics(
        this IEndpointRouteBuilder endpoints,
        string path = DefaultMetricsPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return endpoints
            .MapPrometheusScrapingEndpoint(path)
            .AllowAnonymous();
    }
}
