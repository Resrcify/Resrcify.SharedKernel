using System;
using Microsoft.AspNetCore.Builder;
using Resrcify.SharedKernel.Observability.Logging;
using Serilog.AspNetCore;

namespace Resrcify.SharedKernel.Observability.Extensions;

public static class ServiceDefaultsApplicationBuilderExtensions
{
    /// <summary>
    /// Starts a service's request pipeline the same way in every service, in the order that matters:
    /// <list type="number">
    /// <item><c>UseServiceRequestLogging(configureRequestLogging)</c>: one line per request (none for the probes and
    /// the scrape);</item>
    /// <item><c>UseExceptionHandler()</c>: an unhandled exception answered with problem details (SharedKernel's
    /// <c>ResultExceptionHandler</c>, which <c>services.AddResultProblemDetails()</c> registers), and a request the
    /// client gave up on with a 499.</item>
    /// </list>
    /// The request logging wraps the exception handler, so a request's line carries the status the client got (the 500
    /// the handler answered, a 499) and an exception's stack is logged once, by the handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call it first, before anything else the service adds (HTTPS redirection, status code pages, routing, CORS,
    /// authentication, the endpoints): they then run inside both, so their exceptions are answered and logged the same
    /// way. The endpoints SharedKernel maps (<c>MapHealthEndpoints()</c>, <c>MapServiceMetrics()</c>) aren't part of
    /// it: an endpoint's place in <c>Program.cs</c> doesn't matter, and the service keeps their options and the
    /// builders they return.
    /// </para>
    /// <para>
    /// It refuses, when the pipeline is built, to add the request logging a second time (it was called twice, or
    /// <c>UseServiceRequestLogging</c> was called as well, before or after it), which would log every request twice. A
    /// <c>UseExceptionHandler()</c> of the service's own, before or after it, is harmless (the one nearest the endpoint
    /// answers) and isn't looked for: ASP.NET Core leaves no dependable trace of it on the pipeline.
    /// </para>
    /// </remarks>
    /// <param name="app">The application.</param>
    /// <param name="configureRequestLogging">
    /// Changes to the request logging's defaults, as <c>UseServiceRequestLogging</c>'s <c>configure</c>.
    /// </param>
    /// <returns><paramref name="app"/>, to add the service's own middleware after.</returns>
    /// <exception cref="InvalidOperationException">
    /// The pipeline has the request logging already; or, as <c>UseServiceRequestLogging</c>, the service didn't call
    /// <c>AddServiceTelemetry</c> or Serilog isn't its logger; or, as <c>UseExceptionHandler()</c>, it registered no
    /// problem details (<c>services.AddResultProblemDetails()</c>) nor any other way to answer an exception.
    /// </exception>
    public static IApplicationBuilder UseServiceDefaults(
        this IApplicationBuilder app,
        Action<RequestLoggingOptions>? configureRequestLogging = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        ThrowIfRequestLoggingAdded(app);

        ServiceTelemetryApplicationBuilderExtensions.AddRequestLogging(app, configureRequestLogging);
        RequestLoggingMark.Set(app, RequestLoggingMark.ByServiceDefaults);

        app.UseExceptionHandler();
        return app;
    }

    private static void ThrowIfRequestLoggingAdded(
        IApplicationBuilder app)
    {
        var addedBy = RequestLoggingMark.AddedBy(app);
        if (addedBy == RequestLoggingMark.ByServiceDefaults)
            throw new InvalidOperationException(
                "UseServiceDefaults was already called on this pipeline: call it once, or every request is logged " +
                "twice.");

        if (addedBy is not null)
            throw new InvalidOperationException(
                "UseServiceDefaults adds the request logging itself, before UseExceptionHandler(), but " +
                "UseServiceRequestLogging was already called on this pipeline, so every request would be logged " +
                "twice: remove the UseServiceRequestLogging call (and the UseExceptionHandler() after it), and pass " +
                "its configure to UseServiceDefaults.");
    }
}
