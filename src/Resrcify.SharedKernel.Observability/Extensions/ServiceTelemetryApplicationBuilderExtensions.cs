using System;
using System.Net;
using System.Security.Claims;
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
    /// (a 4xx included: a failed result is an answer, not a fault of the service), with
    /// <see cref="RequestLogProperties.RequestHost"/>, <see cref="RequestLogProperties.ClientIp"/> and
    /// <see cref="RequestLogProperties.UserId"/> added, and the <c>TraceId</c> and <c>SpanId</c> of the request's span.
    /// The requests <c>AddServiceTelemetry</c> doesn't trace (<see cref="ServiceTelemetryOptions.UntracedPaths"/>: the
    /// health probes and the scrape) aren't logged at all.
    /// </summary>
    /// <remarks>
    /// Place it where <c>UseSerilogRequestLogging()</c> was: after <c>UseExceptionHandler()</c> (it sees the exception
    /// and logs it with the line), before <c>UseRouting()</c> and the endpoints. <paramref name="configure"/> runs on
    /// options that already hold these defaults (and the host's <c>RequestLoggingOptions</c>, if it configures them):
    /// replace any, or chain onto <see cref="RequestLoggingOptions.EnrichDiagnosticContext"/> to add fields.
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
            logged => logged.UseSerilogRequestLogging(options =>
            {
                options.GetLevel = LevelOf;
                options.EnrichDiagnosticContext = Enrich;
                options.Logger ??= logger;
                configure?.Invoke(options);
            }));
    }

    private static LogEventLevel LevelOf(
        HttpContext context,
        double elapsedMilliseconds,
        Exception? exception)
        => exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : LogEventLevel.Information;

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
