using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Observability.Extensions;
using Resrcify.SharedKernel.Observability.Logging;
using Resrcify.SharedKernel.Observability.UnitTests.Support;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Web.Exceptions;
using Resrcify.SharedKernel.Web.Extensions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Observability.UnitTests.Extensions;

/// <summary>
/// <c>UseServiceRequestLogging</c> around SharedKernel's exception handler (<c>AddResultProblemDetails()</c> +
/// <c>UseExceptionHandler()</c>), in the documented place (before it) and where <c>UseSerilogRequestLogging()</c>
/// usually went (after it): either way a client abort is a 499 at Information, nothing about it an error, and an
/// unhandled exception a 500 at Error whose stack is logged once.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
[Collection(TelemetryCollection.Name)]
public sealed class ServiceTelemetryApplicationBuilderExtensionsPlacementTests
{
    private const string RequestLoggingSource = "Serilog.AspNetCore.RequestLoggingMiddleware";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const string BeforeExceptionHandler = "before UseExceptionHandler()";
    private const string AfterExceptionHandler = "after UseExceptionHandler()";

    public static TheoryData<string> Placements => [BeforeExceptionHandler, AfterExceptionHandler];

    [Theory]
    [MemberData(nameof(Placements))]
    public async Task ARequestTheClientGivesUpOn_ShouldBeA499AtInformation_AndNothingAboutItAnError(
        string placement)
    {
        await using var host = await PlacementHost.StartAsync(placement);
        using var giveUp = new CancellationTokenSource();

        var request = host.Client.GetAsync(new Uri("/waits", UriKind.Relative), giveUp.Token);
        await host.Waiting.Task.WaitAsync(Timeout);
        await giveUp.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(request);
        var line = await host.WaitForRequestAsync("/waits");

        line.Level.ShouldBe(LogEventLevel.Information);
        ScalarOf(line, "StatusCode").ShouldBe(StatusCodes.Status499ClientClosedRequest);
        line.Exception.ShouldBeNull();
        host.Events.ShouldNotContain(logged => logged.Level >= LogEventLevel.Error);
        host.Events.ShouldNotContain(logged => logged.Exception is OperationCanceledException);
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public async Task AnUnhandledException_ShouldBeA500AtError_WithItsStackLoggedOnce(
        string placement)
    {
        await using var host = await PlacementHost.StartAsync(placement);

        using var response = await host.Client.GetAsync(new Uri("/throws", UriKind.Relative));
        var line = await host.WaitForRequestAsync("/throws");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        line.Level.ShouldBe(LogEventLevel.Error);
        ScalarOf(line, "StatusCode").ShouldBe(StatusCodes.Status500InternalServerError);
        var stack = host.Events.Where(logged => logged.Exception is not null).ShouldHaveSingleItem();
        stack.Level.ShouldBe(LogEventLevel.Error);
        stack.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("Boom");
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public async Task AnExceptionLoggedWhereItWasThrown_ShouldNotBeLoggedWithItsStackAgain(
        string placement)
    {
        // As a mediator behavior does: it logs the exception at Error, marks it logged, and lets it through.
        await using var host = await PlacementHost.StartAsync(placement);

        using var response = await host.Client.GetAsync(new Uri("/throws-logged", UriKind.Relative));
        var line = await host.WaitForRequestAsync("/throws-logged");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        line.Level.ShouldBe(LogEventLevel.Error);
        line.Exception.ShouldBeNull();
        var stack = host.Events.Where(logged => logged.Exception is not null).ShouldHaveSingleItem();
        ScalarOf(stack, "SourceContext").ShouldBe(nameof(PlacementHost));
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public async Task ASuccessfulRequest_ShouldStillBeOneLineAtInformation(
        string placement)
    {
        await using var host = await PlacementHost.StartAsync(placement);

        using var response = await host.Client.GetAsync(new Uri("/players", UriKind.Relative));
        var line = await host.WaitForRequestAsync("/players");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        line.Level.ShouldBe(LogEventLevel.Information);
        host.RequestEvents().ShouldHaveSingleItem();
    }

    [Fact]
    public void TheRequestLine_ShouldClaimTheException_AsLoggedExceptionsDoes()
    {
        var target = new CollectingSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(target).CreateLogger();
        var requestLogger = LoggedOnceRequestLogger.Over(logger);
        var exception = new InvalidOperationException("Boom");

        requestLogger.Error(exception, "first");
        requestLogger.Error(exception, "second");

        LoggedExceptions.IsLogged(exception).ShouldBeTrue();
        target.Events.Select(logEvent => logEvent.Exception).ShouldBe([exception, null]);
    }

    [Fact]
    public async Task TheRequestLine_ShouldCarryTheExceptionOnce_WhenManyThreadsLogItAtOnce()
    {
        // LoggedExceptions' lock: of the lines logging one exception at the same moment, exactly one carries it.
        var target = new CollectingSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(target).CreateLogger();
        var requestLogger = LoggedOnceRequestLogger.Over(logger);

        for (var round = 0; round < 50; round++)
        {
            var exception = new InvalidOperationException("Boom");
            using var start = new ManualResetEventSlim();
            var writers = Enumerable
                .Range(0, 8)
                .Select(_ => Task.Run(() =>
                {
                    start.Wait();
                    requestLogger.Error(exception, "concurrent");
                }))
                .ToArray();
            start.Set();
            await Task.WhenAll(writers);

            target.Events.Count(logEvent => ReferenceEquals(logEvent.Exception, exception)).ShouldBe(1);
        }
    }

    private sealed class CollectingSink
        : ILogEventSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyCollection<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent)
            => _events.Enqueue(logEvent);
    }

    private static object? ScalarOf(
        LogEvent logEvent,
        string property)
        => ((ScalarValue)logEvent.Properties[property]).Value;

    /// <summary>A service with SharedKernel's exception handler and the request logging placed as asked.</summary>
    private sealed class PlacementHost(
        WebApplication app,
        HttpClient client,
        CollectingSink sink,
        TaskCompletionSource waiting)
        : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public TaskCompletionSource Waiting { get; } = waiting;

        public IReadOnlyList<LogEvent> Events => [.. sink.Events];

        public static async Task<PlacementHost> StartAsync(
            string placement)
        {
            var sink = new CollectingSink();
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.WebHost.UseTestServer();
            builder.Services.AddServiceTelemetry(
                Telemetry.ServiceName,
                builder.Configuration,
                options =>
                {
                    options.Metrics = false;
                    options.ConfigureLogging = logging => logging
                        .MinimumLevel.Verbose()
                        .WriteTo.Sink(sink);
                });
            builder.Services.AddResultProblemDetails();
            var app = builder.Build();

            if (placement == BeforeExceptionHandler)
            {
                app.UseServiceRequestLogging();
                app.UseExceptionHandler();
            }
            else
            {
                app.UseExceptionHandler();
                app.UseServiceRequestLogging();
            }

            Map(app, waiting);
            await app.StartAsync();
            return new PlacementHost(app, app.GetTestClient(), sink, waiting);
        }

        public IReadOnlyList<LogEvent> RequestEvents()
            => [.. sink.Events.Where(IsRequestEvent)];

        // The line is written as the pipeline unwinds, which may be after the client has its response.
        public async Task<LogEvent> WaitForRequestAsync(
            string path)
        {
            for (var attempt = 0; attempt < 250; attempt++)
            {
                if (RequestEvents().FirstOrDefault(logged => ((ScalarValue)logged.Properties["RequestPath"]).Value as string == path) is { } found)
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

        private static void Map(
            WebApplication app,
            TaskCompletionSource waiting)
        {
            app.MapGet("/players", () => "ok");
            app.MapGet("/throws", string () => throw new InvalidOperationException("Boom"));
            app.MapGet("/throws-logged", string (ILoggerFactory loggers) =>
            {
                var exception = new InvalidOperationException("Logged where it was thrown");
                if (LoggedExceptions.Claim(exception))
                    loggers.CreateLogger(nameof(PlacementHost)).LogError(exception, "The handler threw");
                throw exception;
            });
            app.MapGet("/waits", async (HttpContext context) =>
            {
                waiting.TrySetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, context.RequestAborted);
                return "never";
            });
        }
    }
}
