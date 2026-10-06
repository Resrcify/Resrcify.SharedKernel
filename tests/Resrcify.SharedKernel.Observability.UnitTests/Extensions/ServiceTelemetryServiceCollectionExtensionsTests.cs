using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Quartz.Diagnostics;
using Rebus.Diagnostics;
using Resrcify.SharedKernel.Mediator.Diagnostics;
using Resrcify.SharedKernel.MessageBus.Diagnostics;
using Resrcify.SharedKernel.Observability.Configuration;
using Resrcify.SharedKernel.Observability.Extensions;
using Resrcify.SharedKernel.Observability.Logging;
using Resrcify.SharedKernel.Observability.UnitTests.Support;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Serilog.Events;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Observability.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed partial class ServiceTelemetryServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(MediatorDiagnostics.ActivitySourceName)]
    [InlineData(OutboxDiagnostics.ActivitySourceName)]
    [InlineData(RebusDiagnosticConstants.ActivitySourceName)]
    [InlineData(QuartzInstrumentation.ActivitySourceName)]
    public void AddServiceTelemetry_ShouldTraceTheSource_WhenItIsASharedKernelOrQuartzOne(
        string sourceName)
    {
        var exported = new List<Activity>();
        using var provider = Telemetry.Build(options => options.ConfigureTracing = tracing => tracing.AddInMemoryExporter(exported));
        provider.GetRequiredService<TracerProvider>();

        RecordActivity(sourceName);

        exported.ShouldContain(activity => activity.Source.Name == sourceName);
    }

    [Theory]
    [InlineData(MediatorDiagnostics.MeterName)]
    [InlineData(OutboxDiagnostics.MeterName)]
    [InlineData(MessageBusDiagnostics.MeterName)]
    [InlineData(RebusDiagnosticConstants.MeterName)]
    [InlineData(QuartzInstrumentation.MeterName)]
    public void AddServiceTelemetry_ShouldCollectTheMeter_WhenItIsASharedKernelOrQuartzOne(
        string meterName)
    {
        var exported = new List<Metric>();
        using var provider = Telemetry.Build(options => options.ConfigureMetrics = metrics => metrics.AddInMemoryExporter(exported));
        var meterProvider = provider.GetRequiredService<MeterProvider>();

        var instrument = RecordMeasurement(meterName);
        meterProvider.ForceFlush();

        exported.ShouldContain(metric => metric.MeterName == meterName && metric.Name == instrument);
    }

    [Fact]
    public void AddServiceTelemetry_ShouldTraceAnotherSource_WhenItIsAdded()
    {
        var exported = new List<Activity>();
        using var provider = Telemetry.Build(options =>
        {
            options.ActivitySources.Add("Npgsql");
            options.ConfigureTracing = tracing => tracing.AddInMemoryExporter(exported);
        });
        provider.GetRequiredService<TracerProvider>();

        RecordActivity("Npgsql");

        exported.ShouldContain(activity => activity.Source.Name == "Npgsql");
    }

    [Fact]
    public void AddServiceTelemetry_ShouldCollectAnotherMeter_WhenItIsAdded()
    {
        var exported = new List<Metric>();
        using var provider = Telemetry.Build(options =>
        {
            options.Meters.Add("Npgsql");
            options.ConfigureMetrics = metrics => metrics.AddInMemoryExporter(exported);
        });
        var meterProvider = provider.GetRequiredService<MeterProvider>();

        var instrument = RecordMeasurement("Npgsql");
        meterProvider.ForceFlush();

        exported.ShouldContain(metric => metric.Name == instrument);
    }

    [Fact]
    public void AddServiceTelemetry_ShouldNotTraceTheSharedKernel_WhenItsInstrumentationIsOff()
    {
        var exported = new List<Activity>();
        using var provider = Telemetry.Build(options =>
        {
            options.SharedKernelInstrumentation = false;
            options.ConfigureTracing = tracing => tracing.AddInMemoryExporter(exported);
        });
        provider.GetRequiredService<TracerProvider>();

        RecordActivity(MediatorDiagnostics.ActivitySourceName);
        RecordActivity(RebusDiagnosticConstants.ActivitySourceName);

        exported.ShouldBeEmpty();
    }

    [Fact]
    public void AddServiceTelemetry_ShouldNotTraceQuartz_WhenItsInstrumentationIsOff()
    {
        var exported = new List<Activity>();
        using var provider = Telemetry.Build(options =>
        {
            options.QuartzInstrumentation = false;
            options.ConfigureTracing = tracing => tracing.AddInMemoryExporter(exported);
        });
        provider.GetRequiredService<TracerProvider>();

        RecordActivity(QuartzInstrumentation.ActivitySourceName);

        exported.ShouldBeEmpty();
    }

    [Fact]
    public void AddServiceTelemetry_ShouldNameTheService()
    {
        using var provider = Telemetry.Build();

        var resource = provider.GetRequiredService<TracerProvider>().GetResource();

        resource.Attributes.ShouldContain(new KeyValuePair<string, object>("service.name", Telemetry.ServiceName));
    }

    [Fact]
    public void AddServiceTelemetry_ShouldRegisterNoTracer_WhenTracingIsOff()
    {
        using var provider = Telemetry.Build(options => options.Tracing = false);

        provider.GetService<TracerProvider>().ShouldBeNull();
        provider.GetService<MeterProvider>().ShouldNotBeNull();
    }

    [Fact]
    public void AddServiceTelemetry_ShouldRegisterNoMeterProvider_WhenMetricsAreOff()
    {
        using var provider = Telemetry.Build(options => options.Metrics = false);

        provider.GetService<MeterProvider>().ShouldBeNull();
        provider.GetService<TracerProvider>().ShouldNotBeNull();
    }

    [Fact]
    public void AddServiceTelemetry_ShouldExportTracesOverOtlp_WhenTheEndpointIsSet()
    {
        using var provider = Telemetry.Build(settings: new() { ["Observability:OtlpEndpoint"] = "http://tempo.monitoring:4317" });

        provider.GetRequiredService<IOptionsMonitor<OtlpExporterOptions>>()
            .Get(ServiceTelemetryOptions.OtlpExporterName)
            .Endpoint
            .ShouldBe(new Uri("http://tempo.monitoring:4317"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AddServiceTelemetry_ShouldNotExportOverOtlp_WhenNoEndpointIsSet(
        string? endpoint)
    {
        var services = new ServiceCollection();

        services.AddServiceTelemetry(
            Telemetry.ServiceName,
            Telemetry.Configuration(new() { ["Observability:OtlpEndpoint"] = endpoint }));

        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<OtlpExporterOptions>));
    }

    [Fact]
    public void AddServiceTelemetry_ShouldNotExportOverOtlp_WhenTheExporterIsOff()
    {
        var services = new ServiceCollection();

        services.AddServiceTelemetry(
            Telemetry.ServiceName,
            Telemetry.Configuration(new() { ["Observability:OtlpEndpoint"] = "http://tempo.monitoring:4317" }),
            options => options.OtlpExporter = false);

        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<OtlpExporterOptions>));
    }

    [Theory]
    [InlineData("not a uri")]
    [InlineData("tempo.monitoring:4317")]
    public void AddServiceTelemetry_ShouldFailValidation_WhenTheEndpointIsNotAnHttpUri(
        string endpoint)
    {
        using var provider = Telemetry.Build(settings: new() { ["Observability:OtlpEndpoint"] = endpoint });

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ObservabilityOptions>>().Value);
    }

    [Fact]
    public void AddServiceTelemetry_ShouldLogThroughSerilog_WithTheTraceAndSpanOfTheActivity()
    {
        var sink = new CollectingSink();
        using var provider = Telemetry.Build(options =>
        {
            options.Tracing = false;
            options.Metrics = false;
            options.ConfigureLogging = logging => logging.WriteTo.Sink(sink);
        });
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("test");
        using var activity = new Activity("operation");
        activity.Start();

        LogSomething(logger);

        var logged = sink.Events.ShouldHaveSingleItem();
        ScalarOf(logged, TraceContextEnricher.TraceIdProperty).ShouldBe(activity.TraceId.ToHexString());
        ScalarOf(logged, TraceContextEnricher.SpanIdProperty).ShouldBe(activity.SpanId.ToHexString());
    }

    [Fact]
    public void AddServiceTelemetry_ShouldLeaveTheLoggerAlone_WhenLoggingIsOff()
    {
        using var provider = Telemetry.Build(options => options.Logging = false);

        provider.GetService<ILoggerFactory>().ShouldBeNull();
    }

    [Fact]
    public async Task AddServiceTelemetry_ShouldNotTraceTheProbesAndTheScrape_WhenTheyAreUntracedPaths()
    {
        var exported = new SynchronizedCollection<Activity>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddServiceTelemetry(
            Telemetry.ServiceName,
            builder.Configuration,
            options =>
            {
                options.Logging = false;
                options.Metrics = false;
                options.ConfigureTracing = tracing => tracing.AddInMemoryExporter(exported);
            });
        await using var app = builder.Build();
        app.MapGet("/health/ready", () => "ok");
        app.MapGet("/metrics", () => "ok");
        app.MapGet("/players", () => "ok");
        await app.StartAsync();
        using var client = app.GetTestClient();

        await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        await client.GetAsync(new Uri("/metrics", UriKind.Relative));
        await client.GetAsync(new Uri("/players", UriKind.Relative));
        await WaitUntilAsync(() => PathsOf(exported).Contains("/players"));

        PathsOf(exported).ShouldBe(["/players"]);
    }

    private static void RecordActivity(
        string sourceName)
    {
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("operation");
    }

    private static string RecordMeasurement(
        string meterName)
    {
        var instrument = $"test.counter.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        meter.CreateCounter<long>(instrument).Add(1);
        return instrument;
    }

    private static object? ScalarOf(
        LogEvent logEvent,
        string property)
        => ((ScalarValue)logEvent.Properties[property]).Value;

    private static string[] PathsOf(
        IEnumerable<Activity> activities)
        => activities
            .Select(activity => activity.GetTagItem("url.path") as string)
            .OfType<string>()
            .ToArray();

    private static async Task WaitUntilAsync(
        Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(20);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Something happened")]
    private static partial void LogSomething(ILogger logger);
}
