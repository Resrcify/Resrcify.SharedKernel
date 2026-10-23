using System;

namespace Resrcify.SharedKernel.Web.Exceptions;

/// <summary>
/// How <see cref="ResultExceptionHandler"/> answers an unhandled exception, and whether a request that couldn't be bound
/// reaches it.
/// </summary>
public sealed class ResultProblemDetailsOptions
{
    /// <summary>
    /// Whether the response shows the exception: its type and message as the error's message, and the whole
    /// exception (with its stack) as the problem's <c>detail</c>. <see langword="null"/> (the default): in the
    /// Development environment only. Elsewhere the response says only that something went wrong.
    /// </summary>
    public bool? IncludeExceptionDetails { get; set; }

    /// <summary>
    /// Whether a minimal-API parameter that fails to bind (a required query or body value left out, a route value that
    /// doesn't parse, a body that isn't valid JSON) is answered with problem details in every environment:
    /// <c>AddResultProblemDetails()</c> turns on <c>RouteHandlerOptions.ThrowOnBadRequest</c>, so ASP.NET Core throws its
    /// <c>BadHttpRequestException</c> and <see cref="ResultExceptionHandler"/> answers a 400 (or the exception's 413,
    /// 415 ...) whose one <c>Validation</c> error is coded with the parameter's name. <see langword="true"/> by default.
    /// <see langword="false"/> leaves <c>ThrowOnBadRequest</c> as ASP.NET Core sets it (on in Development only):
    /// elsewhere a binding failure is then an empty 400, logged at <c>Debug</c> by ASP.NET Core. A
    /// <c>BadHttpRequestException</c> that does reach the handler is answered with problem details either way.
    /// </summary>
    public bool ProblemDetailsForBindingFailures { get; set; } = true;

    /// <summary>
    /// How long the <c>Retry-After</c> of a 503 asks a caller to wait: <see cref="ResultExceptionHandler"/> answers an
    /// exception the transient-failure classifier calls transient with a 503 and this, in whole seconds (rounded up;
    /// zero or less is 0). One second by default, the first retry delay of the Web package's resilience preset.
    /// </summary>
    public TimeSpan TransientFailureRetryAfter { get; set; } = TimeSpan.FromSeconds(1);
}
