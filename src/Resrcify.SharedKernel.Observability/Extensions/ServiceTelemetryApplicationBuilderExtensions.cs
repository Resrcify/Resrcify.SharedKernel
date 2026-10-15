using System;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Observability.Configuration;
using Resrcify.SharedKernel.Observability.Logging;
using Serilog;
using Serilog.AspNetCore;
using Serilog.Events;
using Serilog.Extensions.Hosting;

namespace Resrcify.SharedKernel.Observability.Extensions;

public static class ServiceTelemetryApplicationBuilderExtensions
{
    /// <summary>
    /// Logs one line per request with Serilog's request logging (<c>UseSerilogRequestLogging</c>), the same way in every
    /// service: <c>HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed} ms</c>, at
    /// <see cref="LogEventLevel.Error"/> for a 5xx or an exception and <see cref="LogEventLevel.Information"/> otherwise
    /// (a 4xx included: a failed result is an answer, not a fault of the service; and a request the client gave up on,
    /// a 499), with <see cref="RequestLogProperties.RequestHost"/>, <see cref="RequestLogProperties.ClientIp"/> and
    /// <see cref="RequestLogProperties.UserId"/> added, and the <c>TraceId</c> and <c>SpanId</c> of the request's span.
    /// The requests <c>AddServiceTelemetry</c> doesn't trace (<see cref="ServiceTelemetryOptions.UntracedPaths"/>: the
    /// health probes and the scrape) aren't logged at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Place it <b>before</b> <c>UseExceptionHandler()</c> (and before <c>UseRouting()</c> and the endpoints), so the line
    /// logs the status the exception handler answered: a 500 for an unhandled exception, whose stack the handler logs
    /// (once, with SharedKernel's <c>ResultExceptionHandler</c>, or not at all when a mediator behavior logged it
    /// already), and a 499 for a request the client gave up on. Placed after it, where <c>UseSerilogRequestLogging()</c>
    /// usually went, Serilog's middleware sees the exception itself and logs it as "responded 500", whatever the
    /// handler then answers. It still logs correctly there: its own branch answers an
    /// <see cref="OperationCanceledException"/> (or <see cref="System.IO.IOException"/>) thrown once
    /// <see cref="HttpContext.RequestAborted"/> is cancelled with a 499, logged at
    /// <see cref="LogEventLevel.Information"/> with no exception, as <c>UseExceptionHandler()</c> would answer it; and
    /// a real exception's line carries its stack only when nothing logged it before (the logged-once convention of
    /// SharedKernel's <c>LoggedExceptions</c>), so the stack is logged once either way.
    /// </para>
    /// <para>
    /// <paramref name="configure"/> runs on options that already hold these defaults (and the host's
    /// <c>RequestLoggingOptions</c>, if it configures them): replace any, or chain onto
    /// <see cref="RequestLoggingOptions.EnrichDiagnosticContext"/> to add fields. A <see cref="RequestLoggingOptions.Logger"/>
    /// it sets is still written through with the logged-once convention.
    /// </para>
    /// </remarks>
    /// <param name="app">The application.</param>
    /// <param name="configure">Changes to the defaults.</param>
    /// <exception cref="InvalidOperationException">
    /// The service didn't call <c>AddServiceTelemetry</c>, or Serilog isn't its logger (<c>AddServiceTelemetry</c>'s
    /// <see cref="ServiceTelemetryOptions.Logging"/> is off and it didn't call <c>AddSerilog</c> itself).
    /// </exception>
    public static IApplicationBuilder UseServiceRequestLogging(
        this IApplicationBuilder app,
        Action<RequestLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var services = app.ApplicationServices;
        var untraced = services.GetService<UntracedRequests>()
            ?? throw new InvalidOperationException(
                "UseServiceRequestLogging leaves out the requests AddServiceTelemetry doesn't trace, but the " +
                "service didn't call services.AddServiceTelemetry(...).");
        if (services.GetService<DiagnosticContext>() is null)
            throw new InvalidOperationException(
                "UseServiceRequestLogging logs through Serilog, which isn't the service's logger: keep " +
                "AddServiceTelemetry's Logging on (the default), or call services.AddSerilog(...).");

        // The host's own logger, not the static Log.Logger, which another host in the process (a test's) may replace.
        var logger = services.GetService<ILogger>();

        return app.UseWhen(
            context => !untraced.Contains(context.Request.Path),
            logged =>
            {
                logged.UseSerilogRequestLogging(options =>
                {
                    options.GetLevel = LevelOf;
                    options.EnrichDiagnosticContext = Enrich;
                    options.Logger ??= logger;
                    configure?.Invoke(options);
                    options.Logger = LoggedOnceRequestLogger.Over(options.Logger);
                });

                // Inside Serilog's middleware, so a client abort reaches it as a 499 rather than as an exception, which it
                // would log as "responded 500".
                logged.Use(AnswerClientAbortAsync);
            });
    }

    private static async Task AnswerClientAbortAsync(
        HttpContext context,
        RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (IsClientAbort(context, exception))
        {
            // Nobody is waiting for an answer; UseExceptionHandler() would answer it the same way.
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
    }

    // As UseExceptionHandler() tells a client abort from a failure.
    private static bool IsClientAbort(
        HttpContext context,
        Exception exception)
        => exception is OperationCanceledException or IOException
            && context.RequestAborted.IsCancellationRequested;

    private static LogEventLevel LevelOf(
        HttpContext context,
        double elapsedMilliseconds,
        Exception? exception)
    {
        if (exception is not null && IsClientAbort(context, exception))
            return LogEventLevel.Information;

        return exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : LogEventLevel.Information;
    }

    private static void Enrich(
        IDiagnosticContext diagnostics,
        HttpContext context)
    {
        if (context.Request.Host.HasValue)
            diagnostics.Set(RequestLogProperties.RequestHost, context.Request.Host.Value);

        if (ClientIpOf(context) is { } clientIp)
            diagnostics.Set(RequestLogProperties.ClientIp, clientIp.ToString());

        if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
            diagnostics.Set(RequestLogProperties.UserId, userId);
    }

    private static IPAddress? ClientIpOf(
        HttpContext context)
        => context.Connection.RemoteIpAddress is { IsIPv4MappedToIPv6: true } mapped
            ? mapped.MapToIPv4()
            : context.Connection.RemoteIpAddress;
}
