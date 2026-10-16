using System;
using System.Collections.Generic;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

namespace Resrcify.SharedKernel.Observability.Configuration;

/// <summary>
/// What <c>AddServiceTelemetry</c> wires: everything is on by default, and each part can be turned off. The OTLP
/// exporter also needs <c>Observability:OtlpEndpoint</c> (<see cref="ObservabilityOptions"/>).
/// </summary>
public sealed class ServiceTelemetryOptions
{
    /// <summary>
    /// The name of the <c>OtlpExporterOptions</c> the trace exporter reads: anything besides the endpoint (protocol,
    /// headers, timeout) is set with <c>services.Configure&lt;OtlpExporterOptions&gt;(OtlpExporterName, …)</c>.
    /// </summary>
    public const string OtlpExporterName = "ServiceTelemetry";

    /// <summary>OpenTelemetry tracing.</summary>
    public bool Tracing { get; set; } = true;

    /// <summary>OpenTelemetry metrics.</summary>
    public bool Metrics { get; set; } = true;

    /// <summary>
    /// Serilog as the logger: <c>ReadFrom.Configuration</c> (the <c>Serilog</c> section), its self-log to standard
    /// error, and every event enriched with <c>TraceId</c> and <c>SpanId</c> so logs correlate with traces.
    /// </summary>
    public bool Logging { get; set; } = true;

    /// <summary>Incoming requests: spans and the <c>http.server.*</c> metrics.</summary>
    public bool AspNetCoreInstrumentation { get; set; } = true;

    /// <summary>Outgoing <see cref="System.Net.Http.HttpClient"/> calls: spans and the <c>http.client.*</c> metrics.</summary>
    public bool HttpClientInstrumentation { get; set; } = true;

    /// <summary>The .NET runtime's metrics (GC, thread pool, exceptions…).</summary>
    public bool RuntimeInstrumentation { get; set; } = true;

    /// <summary>
    /// Every SharedKernel source and meter (<see cref="TelemetrySources.SharedKernel"/>), and Rebus.Diagnostics
    /// (<see cref="TelemetrySources.RebusDiagnostics"/>), which the message bus turns on.
    /// </summary>
    public bool SharedKernelInstrumentation { get; set; } = true;

    /// <summary>Quartz 4's source and meter (<see cref="TelemetrySources.Quartz"/>).</summary>
    public bool QuartzInstrumentation { get; set; } = true;

    /// <summary>
    /// Npgsql's activity source (<see cref="TelemetrySources.Npgsql"/>): a span per database command. Harmless in a
    /// service without PostgreSQL.
    /// </summary>
    public bool NpgsqlInstrumentation { get; set; } = true;

    /// <summary>
    /// The Prometheus exporter, scraped at the endpoint <c>MapServiceMetrics()</c> maps. The service references
    /// <c>OpenTelemetry.Exporter.Prometheus.AspNetCore</c> itself (it is only published as a prerelease).
    /// </summary>
    public bool PrometheusExporter { get; set; } = true;

    /// <summary>Traces exported over OTLP, when <c>Observability:OtlpEndpoint</c> is set.</summary>
    public bool OtlpExporter { get; set; } = true;

    /// <summary>
    /// Requests that aren't traced, nor logged by <c>UseServiceRequestLogging()</c>, by path prefix (whole segments,
    /// ignoring case): the scrape endpoint and the health probes (<c>/health</c>, <c>/health/ready</c>,
    /// <c>/health/live</c>) by default, which would otherwise fill the traces and the logs every few seconds. Their
    /// metrics are still recorded. A service serving its metrics elsewhere
    /// (<c>MapServiceMetrics("/internal/metrics")</c>) adds that path.
    /// </summary>
    public ICollection<string> UntracedPaths { get; } = ["/metrics", "/health"];

    /// <summary>More activity sources to trace, e.g. <c>"StackExchange.Redis"</c>. Wildcards (<c>*</c>) are allowed.</summary>
    public ICollection<string> ActivitySources { get; } = [];

    /// <summary>More meters to collect, e.g. <c>"Npgsql"</c>. Wildcards (<c>*</c>) are allowed.</summary>
    public ICollection<string> Meters { get; } = [];

    /// <summary>Runs last on the tracer: anything else (e.g. <c>AddRedisInstrumentation()</c>, another exporter).</summary>
    public Action<TracerProviderBuilder>? ConfigureTracing { get; set; }

    /// <summary>Runs last on the meter provider: anything else (views, another exporter).</summary>
    public Action<MeterProviderBuilder>? ConfigureMetrics { get; set; }

    /// <summary>Runs last on the Serilog configuration, after the configuration file and the enrichment.</summary>
    public Action<LoggerConfiguration>? ConfigureLogging { get; set; }

    /// <summary>
    /// How deep a destructured property (<c>{@...}</c>) is written (4 by default), so a whole event message destructured
    /// by mistake stays a few KB. Set before the <c>Serilog</c> section is read: its <c>Destructure</c> entries, or
    /// <see cref="ConfigureLogging"/>, override it. <see langword="null"/> keeps Serilog's own (10).
    /// </summary>
    public int? MaximumDestructuringDepth { get; set; } = 4;

    /// <summary>
    /// How many items of a destructured collection are written (20 by default). Overridable as
    /// <see cref="MaximumDestructuringDepth"/>; <see langword="null"/> writes them all.
    /// </summary>
    public int? MaximumDestructuredCollectionCount { get; set; } = 20;

    /// <summary>
    /// How many characters of a destructured string are written (1,024 by default). Overridable as
    /// <see cref="MaximumDestructuringDepth"/>; <see langword="null"/> writes them all.
    /// </summary>
    public int? MaximumDestructuredStringLength { get; set; } = 1024;
}
