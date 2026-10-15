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
  - [Request logging](#request-logging)
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

`app.UseServiceRequestLogging(configure?)` logs one line per request with Serilog's request logging, leaving out the
probes and the scrape (the same `UntracedPaths` the tracing leaves out), at `Error` for a 5xx or an exception, with the
host, the client's IP, the user id and the request's trace: see [Request logging](#request-logging).

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
<!-- For UseServiceRequestLogging only (its RequestLoggingOptions is in the signature): -->
<PackageReference Include="Serilog.AspNetCore" />
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
app.UseServiceRequestLogging();   // before UseExceptionHandler(): it logs the status the handler answers
app.UseExceptionHandler();
app.UseRouting();
// ...
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
`services.Configure<OtlpExporterOptions>(ServiceTelemetryOptions.OtlpExporterName, otlp => …)`. Switched to HTTP/protobuf
(there or by `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`), the configured endpoint gets the traces path, so
`http://tempo.monitoring:4318` posts to `http://tempo.monitoring:4318/v1/traces`; an endpoint set on the named options
is used as is.

Logs carry `TraceId` and `SpanId` as properties: a JSON formatter writes them, a text template shows them with
`{TraceId}`. A service using Serilog.Enrichers.Span's `WithSpan` can drop it (a property already set is kept).

## Request logging

`app.UseServiceRequestLogging()` (`Resrcify.SharedKernel.Observability.Extensions`) replaces
`app.UseSerilogRequestLogging()`. Place it **before `UseExceptionHandler()`** (and before `UseRouting()`):

```csharp
app.UseServiceRequestLogging();
app.UseExceptionHandler();
```

There it logs the status the exception handler answered: a 500 for an unhandled exception (whose stack the handler logs,
once: Web's `ResultExceptionHandler` follows the logged-once convention, so after a mediator behavior has logged it,
only a `Debug` line), and a 499 for a request the client gave up on. Placed after `UseExceptionHandler()`, where
`UseSerilogRequestLogging()` usually went, Serilog's middleware sees the exception itself, and Serilog logs every
exception passing through it as "responded 500", at `Error`, a client abort (an `OperationCanceledException` once
`RequestAborted` is cancelled) included. It still logs correctly there, so a service that hasn't moved it isn't
misled: its own branch answers a client abort (an `OperationCanceledException` or `IOException` thrown once
`RequestAborted` is cancelled, as `UseExceptionHandler()` tells one) with a 499, logged at `Information` with no
exception, and the line of a real exception carries its stack only when nothing logged it before (the
`LoggedExceptions` convention, followed by name), so the stack is logged once either way.

One line per request, Serilog's `HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms`:

| Request | Line |
|---|---|
| `UntracedPaths` (`/health`, `/health/ready`, `/health/live`, `/metrics` by default) | none |
| 5xx, or an exception (the exception handler then answers 500) | `Error` |
| a request the client gave up on (499) | `Information` |
| anything else, 4xx included | `Information` |

- **The probes and the scrape aren't logged at all**, rather than at `Verbose`: they come every few seconds and say
  nothing a metric doesn't. They are the paths the tracing leaves out, from the same list (`UntracedPaths`), so a path
  added there (a metrics endpoint mapped elsewhere) leaves both.
- **A 4xx is `Information`**: in these services a 4xx is a failed result answered as problem details (validation,
  not found, conflict, unauthorized), an expected answer rather than a fault of the service, and it would bury the
  real warnings. A service that wants them louder sets `GetLevel` (below).
- **Properties** (`RequestLogProperties`): `RequestHost` (the `Host` header), `ClientIp` (the connection's remote
  address, IPv4 written as IPv4; behind a proxy, the proxy's unless `UseForwardedHeaders()` runs first) and `UserId`
  (the `ClaimTypes.NameIdentifier` claim, the one Web's `GetUserId()` reads, when the request is authenticated; read
  when the request ends, so wherever `UseAuthentication()` is). Every line also carries `TraceId` and `SpanId`, those of
  the request's span, from `AddServiceTelemetry`'s enricher.
- It writes through the host's own Serilog logger, not the static `Log.Logger` (or the `Logger` `configure` sets),
  following the logged-once convention for an exception's stack.

It needs `AddServiceTelemetry` (for the paths) with Serilog as the logger (`Logging` on, or the service's own
`AddSerilog`); without them it throws `InvalidOperationException` saying which. `configure` runs on Serilog's
`RequestLoggingOptions` already holding these defaults: replace one, or chain onto the enrichment to add a field:

```csharp
app.UseServiceRequestLogging(logging =>
{
    logging.GetLevel = (context, _, exception) => exception is not null || context.Response.StatusCode >= 500
        ? LogEventLevel.Error
        : context.Response.StatusCode >= 400 ? LogEventLevel.Warning : LogEventLevel.Information;

    var enrich = logging.EnrichDiagnosticContext;
    logging.EnrichDiagnosticContext = (diagnostics, context) =>
    {
        enrich?.Invoke(diagnostics, context);
        diagnostics.Set("UserAgent", context.Request.Headers.UserAgent.ToString());
    };
});
```

## Turning parts off, adding more

| Option | Default | |
|---|---|---|
| `Tracing`, `Metrics`, `Logging` | on | The three parts. |
| `AspNetCoreInstrumentation`, `HttpClientInstrumentation`, `RuntimeInstrumentation` | on | The instrumentation libraries. |
| `SharedKernelInstrumentation` | on | `Resrcify.SharedKernel.*` and Rebus.Diagnostics. |
| `QuartzInstrumentation` | on | Quartz 4's source and meter. |
| `PrometheusExporter` | on | The exporter `MapServiceMetrics` serves. |
| `OtlpExporter` | on | Still needs `Observability:OtlpEndpoint`. |
| `UntracedPaths` | `/metrics`, `/health` | Request path prefixes left out of the traces and the request log (their metrics stay). |
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
app.UseSerilogRequestLogging();
// ...
app.MapPrometheusScrapingEndpoint().AllowAnonymous();
```

After:

```csharp
// InfrastructureServiceRegistration
services.AddServiceTelemetry("shardmanagement", configuration, telemetry =>
    telemetry.ActivitySources.Add("Npgsql"));

// Program.cs
app.UseServiceRequestLogging();
// ...
app.MapServiceMetrics();
```

Then delete the service's `ObservabilityOptions` class. Keep its package references (the ones listed under
[Install](#install), the Serilog sinks and the Prometheus exporter), since this package's are private; keep
`Serilog.AspNetCore` for `UseServiceRequestLogging`, or drop it if the service logs no requests and
`Serilog.Extensions.Hosting` is all it used. The configuration doesn't change. What the service gains: the mediator's spans and metrics and the Quartz meter (which
most services didn't subscribe to), trace ids on every log line, no spans and no request lines for the probes and the
scrape, and the host, client IP and user id on every request line.

## Related modules

- `Resrcify.SharedKernel.Web`: `MapHealthEndpoints()` for the `/health`, `/health/ready` and `/health/live` probes.
- `Resrcify.SharedKernel.Mediator`, `Resrcify.SharedKernel.UnitOfWork`, `Resrcify.SharedKernel.MessageBus`: the
  sources and meters this package subscribes to (their `*Diagnostics` classes describe what each records).
