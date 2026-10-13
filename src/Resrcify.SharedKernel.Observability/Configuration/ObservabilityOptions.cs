using System;

namespace Resrcify.SharedKernel.Observability.Configuration;

/// <summary>
/// The <c>Observability</c> configuration section, as the services already have it: a service moving to
/// <c>AddServiceTelemetry</c> keeps its configuration.
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>
    /// Where traces are exported over OTLP (e.g. <c>http://tempo.monitoring:4317</c>). Empty: traces aren't exported
    /// (local development); the Prometheus metrics are scraped either way.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>The OTLP endpoint as an absolute http(s) URI; <see langword="null"/> when it is empty or not one.</summary>
    internal Uri? OtlpEndpointUri
        => Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out var endpoint)
            && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps)
                ? endpoint
                : null;

    /// <summary>Empty, or an absolute http(s) URI.</summary>
    internal bool IsValid
        => string.IsNullOrWhiteSpace(OtlpEndpoint) || OtlpEndpointUri is not null;
}
