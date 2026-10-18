using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Resrcify.SharedKernel.Observability.Extensions;
using Resrcify.SharedKernel.Observability.UnitTests.Support;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Exceptions;
using Resrcify.SharedKernel.Web.Extensions;
using Serilog.Events;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Observability.UnitTests.Extensions;

/// <summary>
/// <c>UseServiceDefaults()</c> over the real middleware: the request logging outside the exception handler, the handler
/// on every path (the unlogged ones too), the request logging's options passed through, and a second request logging
/// refused when the pipeline is built. The outcomes every placement shares (a 500 with one stack, a client abort's 499,
/// a successful request's line) are in <see cref="ServiceTelemetryApplicationBuilderExtensionsPlacementTests"/>.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
[Collection(TelemetryCollection.Name)]
public sealed class ServiceDefaultsApplicationBuilderExtensionsTests
{
    private const string RequestLoggingSource = "Serilog.AspNetCore.RequestLoggingMiddleware";

    [Fact]
    public async Task UseServiceDefaults_ShouldPutTheRequestLoggingOutsideTheExceptionHandler()
    {
        // Thrown by the service's own middleware, after UseServiceDefaults: inside both. Were the request logging inside
        // the handler, its line would see the exception and carry the stack, and the handler would only log a Debug line.
        await using var host = await DefaultsHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/middleware-throws", UriKind.Relative));
        var line = await host.WaitForRequestAsync("/middleware-throws");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync()).ShouldContain(ErrorCodes.Request.Unhandled);
        line.Level.ShouldBe(LogEventLevel.Error);
        ScalarOf(line, "StatusCode").ShouldBe(StatusCodes.Status500InternalServerError);
        line.Exception.ShouldBeNull();
        var stack = host.Events.Where(logged => logged.Exception is not null).ShouldHaveSingleItem();
        ScalarOf(stack, "SourceContext").ShouldBe(typeof(ResultExceptionHandler).FullName);
        stack.Level.ShouldBe(LogEventLevel.Error);
    }

    [Fact]
    public async Task UseServiceDefaults_ShouldAnswerAnException_OnAPathTheRequestLoggingLeavesOut()
    {
        // The exception handler is on the whole pipeline, not only on the requests the request logging logs.
        await using var host = await DefaultsHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/health/throws", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        host.RequestEvents().ShouldBeEmpty();
        var stack = host.Events.Where(logged => logged.Exception is not null).ShouldHaveSingleItem();
        ScalarOf(stack, "SourceContext").ShouldBe(typeof(ResultExceptionHandler).FullName);
    }

    [Fact]
    public async Task UseServiceDefaults_ShouldPassTheRequestLoggingsOptionsThrough()
    {
        await using var host = await DefaultsHost.StartAsync(app => app.UseServiceDefaults(logging =>
            logging.GetLevel = (_, _, _) => LogEventLevel.Warning));

        using var response = await host.Client.GetAsync(new Uri("/players", UriKind.Relative));
        var line = await host.WaitForRequestAsync("/players");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        line.Level.ShouldBe(LogEventLevel.Warning);
        host.RequestEvents().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task UseServiceDefaults_ShouldThrow_WhenCalledTwice()
    {
        await using var app = DefaultsHost.Build();
        app.UseServiceDefaults();

        Should.Throw<InvalidOperationException>(() => app.UseServiceDefaults())
            .Message.ShouldContain("UseServiceDefaults was already called");
    }

    [Fact]
    public async Task UseServiceDefaults_ShouldThrow_WhenUseServiceRequestLoggingWasCalledBefore()
    {
        await using var app = DefaultsHost.Build();
        app.UseServiceRequestLogging();
        app.UseExceptionHandler();

        Should.Throw<InvalidOperationException>(() => app.UseServiceDefaults())
            .Message.ShouldContain("UseServiceRequestLogging was already called");
    }

    [Fact]
    public async Task UseServiceRequestLogging_ShouldThrow_WhenUseServiceDefaultsAddedItAlready()
    {
        await using var app = DefaultsHost.Build();
        app.UseServiceDefaults();

        Should.Throw<InvalidOperationException>(() => app.UseServiceRequestLogging())
            .Message.ShouldContain("UseServiceDefaults already added the request logging");
    }

    [Fact]
    public async Task UseServiceRequestLogging_ShouldThrow_WhenCalledTwice()
    {
        await using var app = DefaultsHost.Build();
        app.UseServiceRequestLogging();

        Should.Throw<InvalidOperationException>(() => app.UseServiceRequestLogging())
            .Message.ShouldContain("UseServiceRequestLogging was already called");
    }

    [Fact]
    public async Task UseServiceDefaults_ShouldRefuseASecondRequestLogging_InABranchBuiltAfterIt()
    {
        // The branch's requests pass the pipeline's request logging already.
        await using var app = DefaultsHost.Build();
        app.UseServiceDefaults();

        Should.Throw<InvalidOperationException>(() =>
            app.UseWhen(_ => true, branch => branch.UseServiceRequestLogging()));
    }

    [Fact]
    public async Task UseServiceDefaults_ShouldAcceptAnExceptionHandlerOfTheServicesOwnBeforeIt()
    {
        // Harmless: the handler nearest the endpoint, UseServiceDefaults', answers; the other never sees the exception.
        await using var host = await DefaultsHost.StartAsync(app =>
        {
            app.UseExceptionHandler();
            app.UseServiceDefaults();
        });

        using var response = await host.Client.GetAsync(new Uri("/throws", UriKind.Relative));
        var line = await host.WaitForRequestAsync("/throws");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        line.Exception.ShouldBeNull();
        host.Events.Where(logged => logged.Exception is not null).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task UseServiceDefaults_ShouldThrow_WhenTheServiceDidNotAddServiceTelemetry()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddResultProblemDetails();
        await using var app = builder.Build();

        Should.Throw<InvalidOperationException>(() => app.UseServiceDefaults())
            .Message.ShouldContain("AddServiceTelemetry");
    }

    private static object? ScalarOf(
        LogEvent logEvent,
        string property)
        => ((ScalarValue)logEvent.Properties[property]).Value;

    /// <summary>
    /// A service as its <c>Program.cs</c> wires it with SharedKernel: <c>AddServiceTelemetry</c>,
    /// <c>AddResultProblemDetails()</c>, <c>UseServiceDefaults()</c> (or the pipeline a test asks for), then its own
    /// middleware and endpoints.
    /// </summary>
    private sealed class DefaultsHost(
        WebApplication app,
        HttpClient client,
        CollectingSink sink)
        : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public IReadOnlyList<LogEvent> Events => [.. sink.Events];

        public static WebApplication Build(
            CollectingSink? sink = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.WebHost.UseTestServer();
            builder.Services.AddServiceTelemetry(
                Telemetry.ServiceName,
                builder.Configuration,
                options =>
                {
                    options.Metrics = false;
                    options.ConfigureLogging = logging =>
                    {
                        logging.MinimumLevel.Verbose();
                        if (sink is not null)
                            logging.WriteTo.Sink(sink);
                    };
                });
            builder.Services.AddResultProblemDetails();
            return builder.Build();
        }

        public static async Task<DefaultsHost> StartAsync(
            Action<WebApplication>? pipeline = null)
        {
            var sink = new CollectingSink();
            var app = Build(sink);

            if (pipeline is null)
                app.UseServiceDefaults();
            else
                pipeline(app);

            app.Use(ThrowOnMiddlewarePath);
            Map(app);
            await app.StartAsync();
            return new DefaultsHost(app, app.GetTestClient(), sink);
        }

        public IReadOnlyList<LogEvent> RequestEvents()
            => [.. sink.Events.Where(IsRequestEvent)];

        // The line is written as the pipeline unwinds, which may be after the client has its response.
        public async Task<LogEvent> WaitForRequestAsync(
            string path)
        {
            for (var attempt = 0; attempt < 250; attempt++)
            {
                if (RequestEvents().FirstOrDefault(logged => ScalarOf(logged, "RequestPath") as string == path) is { } found)
                    return found;
                await Task.Delay(20);
            }

            throw new ShouldAssertException($"No request line was logged for {path}.");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }

        private static bool IsRequestEvent(
            LogEvent logEvent)
            => logEvent.Properties.TryGetValue("SourceContext", out var source)
                && source is ScalarValue { Value: RequestLoggingSource };

        private static Task ThrowOnMiddlewarePath(
            HttpContext context,
            RequestDelegate next)
            => context.Request.Path == "/middleware-throws"
                ? throw new InvalidOperationException("Boom in a middleware")
                : next(context);

        private static void Map(
            WebApplication app)
        {
            app.MapGet("/players", () => "ok");
            app.MapGet("/throws", string () => throw new InvalidOperationException("Boom"));
            app.MapGet("/health/throws", string () => throw new InvalidOperationException("Boom on a probe"));
        }
    }
}
