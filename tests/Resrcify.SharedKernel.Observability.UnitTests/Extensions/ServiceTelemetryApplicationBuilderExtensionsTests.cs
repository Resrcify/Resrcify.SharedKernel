using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Resrcify.SharedKernel.Observability.Configuration;
using Resrcify.SharedKernel.Observability.Extensions;
using Resrcify.SharedKernel.Observability.Logging;
using Resrcify.SharedKernel.Observability.UnitTests.Support;
using Serilog.AspNetCore;
using Serilog.Events;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Observability.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
[Collection(TelemetryCollection.Name)]
public sealed class ServiceTelemetryApplicationBuilderExtensionsTests
{
    private const string RequestLoggingSource = "Serilog.AspNetCore.RequestLoggingMiddleware";
    private const string UserHeader = "X-Test-User";
    private const string ClientIp = "10.1.2.3";

    [Fact]
    public async Task UseServiceRequestLogging_ShouldLogNothing_ForTheProbesAndTheScrape()
    {
        await using var host = await RequestLoggingHost.StartAsync();

        await host.GetAsync("/health");
        await host.GetAsync("/health/ready");
        await host.GetAsync("/health/live");
        await host.GetAsync("/metrics");
        await host.GetAsync("/players");
        await host.WaitForRequestAsync("/players");

        host.RequestEvents().Select(PathOf).ShouldBe(["/players"]);
    }

    [Fact]
    public async Task UseServiceRequestLogging_ShouldLeaveOutAnotherUntracedPath_WhenOneIsAdded()
    {
        await using var host = await RequestLoggingHost.StartAsync(
            telemetry: telemetry => telemetry.UntracedPaths.Add("/internal"));

        await host.GetAsync("/internal/metrics");
        await host.GetAsync("/players");
        await host.WaitForRequestAsync("/players");

        host.RequestEvents().Select(PathOf).ShouldBe(["/players"]);
    }

    [Theory]
    [InlineData("/players", 200, LogEventLevel.Information)]
    [InlineData("/missing", 404, LogEventLevel.Information)]
    [InlineData("/broken", 500, LogEventLevel.Error)]
    public async Task UseServiceRequestLogging_ShouldLogOneLine_AtTheLevelOfItsStatus(
        string path,
        int status,
        LogEventLevel level)
    {
        await using var host = await RequestLoggingHost.StartAsync();

        using var response = await host.GetAsync(path);
        var logged = await host.WaitForRequestAsync(path);

        ((int)response.StatusCode).ShouldBe(status);
        host.RequestEvents().ShouldHaveSingleItem();
        logged.Level.ShouldBe(level);
        ScalarOf(logged, "StatusCode").ShouldBe(status);
        ScalarOf(logged, "RequestMethod").ShouldBe("GET");
    }

    [Fact]
    public async Task UseServiceRequestLogging_ShouldLogAnExceptionAtError_WithTheException()
    {
        await using var host = await RequestLoggingHost.StartAsync();

        using var response = await host.GetAsync("/throws");
        var logged = await host.WaitForRequestAsync("/throws");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        logged.Level.ShouldBe(LogEventLevel.Error);
        logged.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("Boom");
        ScalarOf(logged, "StatusCode").ShouldBe(500);
    }

    [Fact]
    public async Task UseServiceRequestLogging_ShouldAddTheHostTheClientTheUserAndTheRequestsTrace()
    {
        var userId = Guid.NewGuid().ToString();
        await using var host = await RequestLoggingHost.StartAsync();

        await host.GetAsync("/players", userId);
        var logged = await host.WaitForRequestAsync("/players");

        ScalarOf(logged, RequestLogProperties.RequestHost).ShouldBe("localhost");
        ScalarOf(logged, RequestLogProperties.ClientIp).ShouldBe(ClientIp);
        ScalarOf(logged, RequestLogProperties.UserId).ShouldBe(userId);
        var span = await host.WaitForSpanAsync("/players");
        ScalarOf(logged, "TraceId").ShouldBe(span.TraceId.ToHexString());
        ScalarOf(logged, "SpanId").ShouldBe(span.SpanId.ToHexString());
    }

    [Fact]
    public async Task UseServiceRequestLogging_ShouldAddNoUserId_WhenTheRequestIsAnonymous()
    {
        await using var host = await RequestLoggingHost.StartAsync();

        await host.GetAsync("/players");
        var logged = await host.WaitForRequestAsync("/players");

        logged.Properties.Keys.ShouldNotContain(RequestLogProperties.UserId);
    }

    [Fact]
    public async Task UseServiceRequestLogging_ShouldApplyTheServicesChanges_AfterTheDefaults()
    {
        await using var host = await RequestLoggingHost.StartAsync(logging =>
        {
            logging.GetLevel = (_, _, _) => LogEventLevel.Warning;
            var enrich = logging.EnrichDiagnosticContext;
            logging.EnrichDiagnosticContext = (diagnostics, context) =>
            {
                enrich?.Invoke(diagnostics, context);
                diagnostics.Set("Tenant", "test");
            };
        });

        await host.GetAsync("/players");
        var logged = await host.WaitForRequestAsync("/players");

        logged.Level.ShouldBe(LogEventLevel.Warning);
        ScalarOf(logged, "Tenant").ShouldBe("test");
        ScalarOf(logged, RequestLogProperties.RequestHost).ShouldBe("localhost");
    }

    [Fact]
    public async Task UseServiceRequestLogging_ShouldThrow_WhenTheServiceDidNotAddServiceTelemetry()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        Should.Throw<InvalidOperationException>(() => app.UseServiceRequestLogging())
            .Message.ShouldContain("AddServiceTelemetry");
    }

    [Fact]
    public async Task UseServiceRequestLogging_ShouldThrow_WhenSerilogIsNotTheLogger()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddServiceTelemetry(
            Telemetry.ServiceName,
            builder.Configuration,
            telemetry => telemetry.Logging = false);
        await using var app = builder.Build();

        Should.Throw<InvalidOperationException>(() => app.UseServiceRequestLogging())
            .Message.ShouldContain("Serilog");
    }

    private static object? ScalarOf(
        LogEvent logEvent,
        string property)
        => ((ScalarValue)logEvent.Properties[property]).Value;

    private static string? PathOf(
        LogEvent logEvent)
        => ScalarOf(logEvent, "RequestPath") as string;

    /// <summary>
    /// A service as Shard's Program wires it: exception handler, then the request log, then the endpoints; every request
    /// comes from <see cref="ClientIp"/> (written mapped to IPv6), and from a signed-in user when it carries
    /// <see cref="UserHeader"/>.
    /// </summary>
    private sealed class RequestLoggingHost(
        WebApplication app,
        HttpClient client,
        CollectingSink sink,
        SynchronizedCollection<Activity> spans)
        : IAsyncDisposable
    {
        public static async Task<RequestLoggingHost> StartAsync(
            Action<RequestLoggingOptions>? configure = null,
            Action<ServiceTelemetryOptions>? telemetry = null)
        {
            var sink = new CollectingSink();
            var spans = new SynchronizedCollection<Activity>();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddServiceTelemetry(
                Telemetry.ServiceName,
                builder.Configuration,
                options =>
                {
                    options.Metrics = false;
                    options.ConfigureLogging = logging => logging.WriteTo.Sink(sink);
                    options.ConfigureTracing = tracing => tracing.AddInMemoryExporter(spans);
                    telemetry?.Invoke(options);
                });
            var app = builder.Build();

            app.UseExceptionHandler(handler => handler.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            }));
            app.Use(SignIn);
            app.UseServiceRequestLogging(configure);
            Map(app);

            await app.StartAsync();
            return new RequestLoggingHost(app, app.GetTestClient(), sink, spans);
        }

        public async Task<HttpResponseMessage> GetAsync(
            string path,
            string? userId = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            if (userId is not null)
                request.Headers.Add(UserHeader, userId);
            return await client.SendAsync(request);
        }

        public IReadOnlyList<LogEvent> RequestEvents()
            => [.. sink.Events.Where(IsRequestEvent)];

        // The line is written as the pipeline unwinds, which may be after the client has its response.
        public async Task<LogEvent> WaitForRequestAsync(
            string path)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (RequestEvents().FirstOrDefault(logged => PathOf(logged) == path) is { } found)
                    return found;
                await Task.Delay(20);
            }

            throw new ShouldAssertException($"No request line was logged for {path}.");
        }

        public async Task<Activity> WaitForSpanAsync(
            string path)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (spans.FirstOrDefault(span => span.GetTagItem("url.path") as string == path) is { } found)
                    return found;
                await Task.Delay(20);
            }

            throw new ShouldAssertException($"No span was exported for {path}.");
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }

        private static bool IsRequestEvent(
            LogEvent logEvent)
            => logEvent.Properties.TryGetValue("SourceContext", out var source)
                && source is ScalarValue { Value: RequestLoggingSource };

        private static Task SignIn(
            HttpContext context,
            RequestDelegate next)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(ClientIp).MapToIPv6();
            if (context.Request.Headers.TryGetValue(UserHeader, out var userId))
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    authenticationType: "Test"));
            return next(context);
        }

        private static void Map(
            WebApplication app)
        {
            app.MapGet("/health", () => "ok");
            app.MapGet("/health/ready", () => "ok");
            app.MapGet("/health/live", () => "ok");
            app.MapGet("/metrics", () => "ok");
            app.MapGet("/internal/metrics", () => "ok");
            app.MapGet("/players", () => "ok");
            app.MapGet("/missing", () => TypedResults.NotFound());
            app.MapGet("/broken", () => TypedResults.StatusCode(StatusCodes.Status500InternalServerError));
            app.MapGet("/throws", string () => throw new InvalidOperationException("Boom"));
        }
    }
}
