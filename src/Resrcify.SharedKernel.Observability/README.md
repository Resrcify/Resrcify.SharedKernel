# Resrcify.SharedKernel.Observability

`Resrcify.SharedKernel.Observability` wires a service's telemetry in one call, the same way in every service:
OpenTelemetry tracing and metrics, the Prometheus scrape endpoint, trace export over OTLP, and Serilog with logs that
lead to their trace.

## Table of Contents

- [Resrcify.SharedKernel.Observability](#resrcifysharedkernelobservability)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
  - [Quick Start](#quick-start)
  - [Configuration](#configuration)
  - [Turning parts off, adding more](#turning-parts-off-adding-more)
  - [Moving a service to it: before and after](#moving-a-service-to-it-before-and-after)
  - [Related modules](#related-modules)

## What you get

`services.AddServiceTelemetry(serviceName, configuration, configure?)` (`Resrcify.SharedKernel.Observability.Extensions`):

- **Tracing**: ASP.NET Core (without the `/metrics` and `/health…` requests: `UntracedPaths`), HttpClient, every
  SharedKernel source (`Resrcify.SharedKernel.*`: the mediator, the outbox), Rebus.Diagnostics (the message bus' send
  and handle spans) and Quartz 4 (a span per job run). The service is named `serviceName` (`service.name`).
- **Metrics**: ASP.NET Core, HttpClient, the .NET runtime, every SharedKernel meter (the mediator, the outbox, the
  message bus), Rebus.Diagnostics and Quartz 4, exported to Prometheus.
- **OTLP**: traces exported to `Observability:OtlpEndpoint` when it is set (the section and key the services already
  have), nothing otherwise.
- **Serilog** as the logger: `ReadFrom.Configuration` (the `Serilog` section), Serilog's self-log to standard error,
  and every event enriched with `TraceId` and `SpanId` (the names Serilog.Enrichers.Span uses).

`app.MapServiceMetrics(path = "/metrics")` maps the Prometheus scrape endpoint, open to anonymous callers whatever the
authorization fallback policy.

The SharedKernel sources and meters are matched by the wildcard `TelemetrySources.SharedKernel`
(`Resrcify.SharedKernel.*`), so the package depends on none of the other SharedKernel packages and a new source in
one of them is traced without a change here. `TelemetrySources.RebusDiagnostics` and `TelemetrySources.Quartz` are the
names those libraries publish (`RebusDiagnosticConstants`, `QuartzInstrumentation`); the tests hold them to it.

## Prerequisites

- .NET 10 SDK, an ASP.NET Core host.
- **The packages it wires, referenced by the service.** Like every SharedKernel package, this one keeps its
  dependencies private, so a service lists what it ships: the OpenTelemetry host, instrumentation and exporter
  packages and the Serilog host packages below. A missing one fails at start-up naming the assembly; the Prometheus
  exporter (only ever published as a prerelease) gets a message of its own, or set `PrometheusExporter = false`.
- A `Serilog` configuration section and the sinks it names (e.g. `Serilog.Sinks.Console`), as today.

## Install

```xml
<PackageReference Include="Resrcify.SharedKernel.Observability" Version="<latest>" />
<!-- What it wires (its own references are private, so the service lists them): -->
<PackageReference Include="OpenTelemetry.Extensions.Hosting" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
<PackageReference Include="Serilog.Extensions.Hosting" />
<PackageReference Include="Serilog.Settings.Configuration" />
<PackageReference Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" Version="1.19.1-beta.1" />
```

Use the versions in SharedKernel's `Directory.Packages.props` (or newer). Most services reference these already.

## Quick Start

```csharp
// Infrastructure
services.AddServiceTelemetry("shardmanagement", configuration, telemetry =>
{
    telemetry.ActivitySources.Add("Npgsql");
    telemetry.Meters.Add("Npgsql");
});

// Program.cs
app.MapServiceMetrics();
```

## Configuration

```json
{
  "Observability": {
    "OtlpEndpoint": "http://tempo.monitoring:4317"
  },
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console" ],
    "WriteTo": [ { "Name": "Console" } ]
  }
}
```

`Observability:OtlpEndpoint` is bound to `ObservabilityOptions` and validated at start-up: empty, or an absolute
`http`/`https` URI. Anything else about the OTLP exporter (protocol, headers) is set on its named options:
`services.Configure<OtlpExporterOptions>(ServiceTelemetryOptions.OtlpExporterName, otlp => …)`.

Logs carry `TraceId` and `SpanId` as properties: a JSON formatter writes them, a text template shows them with
`{TraceId}`. A service using Serilog.Enrichers.Span's `WithSpan` can drop it (a property already set is kept).

## Turning parts off, adding more

| Option | Default | |
|---|---|---|
| `Tracing`, `Metrics`, `Logging` | on | The three parts. |
| `AspNetCoreInstrumentation`, `HttpClientInstrumentation`, `RuntimeInstrumentation` | on | The instrumentation libraries. |
| `SharedKernelInstrumentation` | on | `Resrcify.SharedKernel.*` and Rebus.Diagnostics. |
| `QuartzInstrumentation` | on | Quartz 4's source and meter. |
| `PrometheusExporter` | on | The exporter `MapServiceMetrics` serves. |
| `OtlpExporter` | on | Still needs `Observability:OtlpEndpoint`. |
| `UntracedPaths` | `/metrics`, `/health` | Request path prefixes left out of the traces (their metrics stay). |
| `ActivitySources`, `Meters` | empty | More names to subscribe to, e.g. `Npgsql`; wildcards allowed. |
| `ConfigureTracing`, `ConfigureMetrics`, `ConfigureLogging` | none | Run last on the tracer, the meter provider and the Serilog configuration. |

```csharp
services.AddServiceTelemetry("swgohapi", configuration, telemetry =>
{
    telemetry.QuartzInstrumentation = false;
    telemetry.ConfigureTracing = tracing => tracing.AddRedisInstrumentation();
});
```

## Moving a service to it: before and after

Before (Shard, every service has its own copy):

```csharp
// InfrastructureServiceRegistration
Serilog.Debugging.SelfLog.Enable(Console.Error);
services.AddSerilog(configure => configure.ReadFrom.Configuration(configuration));
services
    .AddOptions<ObservabilityOptions>()
    .BindConfiguration(ObservabilityOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
var observability = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>();
services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("shardmanagement"))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(RebusDiagnosticConstants.MeterName)
        .AddMeter(MessageBusDiagnostics.MeterName)
        .AddMeter(OutboxDiagnostics.MeterName)
        .AddPrometheusExporter())
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource(QuartzInstrumentation.ActivitySourceName)
            .AddSource("Npgsql")
            .AddSource(OutboxDiagnostics.ActivitySourceName)
            .AddSource(RebusDiagnosticConstants.ActivitySourceName);
        if (!string.IsNullOrWhiteSpace(observability?.OtlpEndpoint))
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(observability.OtlpEndpoint));
    });

// Program.cs
app.MapPrometheusScrapingEndpoint().AllowAnonymous();
```

After:

```csharp
// InfrastructureServiceRegistration
services.AddServiceTelemetry("shardmanagement", configuration, telemetry =>
    telemetry.ActivitySources.Add("Npgsql"));

// Program.cs
app.MapServiceMetrics();
```

Then delete the service's `ObservabilityOptions` class. Keep its package references (the ones listed under
[Install](#install), the Serilog sinks and the Prometheus exporter), since this package's are private; drop
`Serilog.AspNetCore` if `Serilog.Extensions.Hosting` is all it used. The configuration doesn't change. What the service gains: the mediator's spans and metrics and the Quartz meter (which
most services didn't subscribe to), trace ids on every log line, and no spans for the probes and the scrape.

## Related modules

- `Resrcify.SharedKernel.Web`: `MapHealthEndpoints()` for the `/health`, `/health/ready` and `/health/live` probes.
- `Resrcify.SharedKernel.Mediator`, `Resrcify.SharedKernel.UnitOfWork`, `Resrcify.SharedKernel.MessageBus`: the
  sources and meters this package subscribes to (their `*Diagnostics` classes describe what each records).
