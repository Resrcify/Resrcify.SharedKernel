using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Resrcify.SharedKernel.Observability.Configuration;
using Resrcify.SharedKernel.Observability.Logging;
using Serilog;
using Serilog.Debugging;

namespace Resrcify.SharedKernel.Observability.Extensions;

public static class ServiceTelemetryServiceCollectionExtensions
{
    private const string OtlpTracesPath = "v1/traces";

    private const string PrometheusExporterType =
        "OpenTelemetry.Exporter.PrometheusAspNetCoreOptions, OpenTelemetry.Exporter.Prometheus.AspNetCore";

    /// <summary>
    /// Wires the service's telemetry the same way in every service: OpenTelemetry tracing and metrics (ASP.NET Core,
    /// HttpClient, the runtime, every SharedKernel source and meter, Rebus.Diagnostics, Quartz), the Prometheus
    /// exporter (map its endpoint with <c>MapServiceMetrics()</c>), traces over OTLP when
    /// <c>Observability:OtlpEndpoint</c> is set, and Serilog from the <c>Serilog</c> section with every event carrying
    /// its <c>TraceId</c> and <c>SpanId</c>. Each part can be turned off, and more sources, meters or anything else
    /// added, through <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceName">The service's name on its traces and metrics (<c>service.name</c>), e.g. <c>"shardmanagement"</c>.</param>
    /// <param name="configuration">The configuration holding the <c>Observability</c> and <c>Serilog</c> sections.</param>
    /// <param name="configure">Changes to the defaults.</param>
    /// <exception cref="InvalidOperationException">
    /// The Prometheus exporter is on but the service doesn't reference <c>OpenTelemetry.Exporter.Prometheus.AspNetCore</c>.
    /// </exception>
    public static IServiceCollection AddServiceTelemetry(
        this IServiceCollection services,
        string serviceName,
        IConfiguration configuration,
        Action<ServiceTelemetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new ServiceTelemetryOptions();
        configure?.Invoke(options);

        var section = configuration.GetSection(ObservabilityOptions.SectionName);
        services
            .AddOptions<ObservabilityOptions>()
            .Bind(section)
            .Validate(
                observability => observability.IsValid,
                $"{ObservabilityOptions.SectionName}:{nameof(ObservabilityOptions.OtlpEndpoint)} must be empty or an absolute http(s) URI.")
            .ValidateOnStart();

        if (options.Logging)
            AddSerilog(services, configuration, options);

        if (options.Tracing || options.Metrics)
            AddOpenTelemetry(services, serviceName, section.Get<ObservabilityOptions>(), options);

        return services;
    }

    private static void AddSerilog(
        IServiceCollection services,
        IConfiguration configuration,
        ServiceTelemetryOptions options)
    {
        // Serilog's own problems (a sink that can't be loaded, a bad configuration) would otherwise go nowhere.
        SelfLog.Enable(Console.Error);

        services.AddSerilog(logger =>
        {
            logger.ReadFrom
                .Configuration(configuration)
                .Enrich.With<TraceContextEnricher>();
            options.ConfigureLogging?.Invoke(logger);
        });
    }

    private static void AddOpenTelemetry(
        IServiceCollection services,
        string serviceName,
        ObservabilityOptions? observability,
        ServiceTelemetryOptions options)
    {
        var builder = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName));

        if (options.Tracing)
            builder.WithTracing(tracing => ConfigureTracing(tracing, options, observability?.OtlpEndpointUri));

        if (options.Tracing && options.OtlpExporter && observability?.OtlpEndpointUri is { } otlpEndpoint)
            services.PostConfigure<OtlpExporterOptions>(
                ServiceTelemetryOptions.OtlpExporterName,
                otlp => AppendTracesPathOverHttp(otlp, otlpEndpoint));

        if (!options.Metrics)
            return;

        if (options.PrometheusExporter)
            EnsurePrometheusExporterIsReferenced();
        builder.WithMetrics(metrics => ConfigureMetrics(metrics, options));
    }

    private static void ConfigureTracing(
        TracerProviderBuilder tracing,
        ServiceTelemetryOptions options,
        Uri? otlpEndpoint)
    {
        if (options.AspNetCoreInstrumentation)
            tracing.AddAspNetCoreInstrumentation(aspNetCore => aspNetCore.Filter = context
                => !IsUntraced(context.Request.Path, options.UntracedPaths));

        if (options.HttpClientInstrumentation)
            tracing.AddHttpClientInstrumentation();

        tracing.AddSource(SourceNames(options).ToArray());

        if (options.OtlpExporter && otlpEndpoint is not null)
            tracing.AddOtlpExporter(
                ServiceTelemetryOptions.OtlpExporterName,
                otlp => otlp.Endpoint = otlpEndpoint);

        options.ConfigureTracing?.Invoke(tracing);
    }

    // An endpoint set in code is used as is, which over HTTP/protobuf (switched on in the named options or through
    // OTEL_EXPORTER_OTLP_PROTOCOL) would post to the collector's root: give it the signal's path, as the exporter does
    // for an endpoint it reads from its environment variable. Runs after every Configure, once the protocol is known;
    // an endpoint the service set itself is left alone.
    private static void AppendTracesPathOverHttp(
        OtlpExporterOptions otlp,
        Uri configuredEndpoint)
    {
        if (otlp.Protocol != OtlpExportProtocol.HttpProtobuf || otlp.Endpoint != configuredEndpoint)
            return;

        var path = otlp.Endpoint.AbsolutePath;
        if (path.EndsWith(OtlpTracesPath, StringComparison.OrdinalIgnoreCase))
            return;

        otlp.Endpoint = new UriBuilder(otlp.Endpoint) { Path = $"{path.TrimEnd('/')}/{OtlpTracesPath}" }.Uri;
    }

    private static void ConfigureMetrics(
        MeterProviderBuilder metrics,
        ServiceTelemetryOptions options)
    {
        if (options.AspNetCoreInstrumentation)
            metrics.AddAspNetCoreInstrumentation();

        if (options.HttpClientInstrumentation)
            metrics.AddHttpClientInstrumentation();

        if (options.RuntimeInstrumentation)
            metrics.AddRuntimeInstrumentation();

        metrics.AddMeter(MeterNames(options).ToArray());

        if (options.PrometheusExporter)
            AddPrometheusExporter(metrics);

        options.ConfigureMetrics?.Invoke(metrics);
    }

    private static IEnumerable<string> SourceNames(
        ServiceTelemetryOptions options)
        => BuiltInNames(options).Concat(options.ActivitySources);

    private static IEnumerable<string> MeterNames(
        ServiceTelemetryOptions options)
        => BuiltInNames(options).Concat(options.Meters);

    // The sources and meters share their names.
    private static IEnumerable<string> BuiltInNames(
        ServiceTelemetryOptions options)
    {
        if (options.SharedKernelInstrumentation)
        {
            yield return TelemetrySources.SharedKernel;
            yield return TelemetrySources.RebusDiagnostics;
        }

        if (options.QuartzInstrumentation)
            yield return TelemetrySources.Quartz;
    }

    private static bool IsUntraced(
        PathString path,
        IEnumerable<string> untracedPaths)
        => untracedPaths.Any(untraced => path.StartsWithSegments(untraced, StringComparison.OrdinalIgnoreCase));

    // The exporter's assembly is the service's own reference (see the csproj): say so at start-up, rather than fail
    // with a missing assembly when the meter provider is first built.
    private static void EnsurePrometheusExporterIsReferenced()
    {
        if (Type.GetType(PrometheusExporterType, throwOnError: false) is null)
            throw new InvalidOperationException(
                "AddServiceTelemetry exports metrics to Prometheus, but the service doesn't reference the " +
                "OpenTelemetry.Exporter.Prometheus.AspNetCore package: add it, or turn the exporter off " +
                "(options.PrometheusExporter = false).");
    }

    // Kept apart, so the exporter's assembly is only loaded once it is known to be there.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddPrometheusExporter(
        MeterProviderBuilder metrics)
        => metrics.AddPrometheusExporter();
}
