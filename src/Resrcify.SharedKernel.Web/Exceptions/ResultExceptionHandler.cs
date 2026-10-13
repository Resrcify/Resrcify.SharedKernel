using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Extensions;

namespace Resrcify.SharedKernel.Web.Exceptions;

/// <summary>
/// Answers an unhandled exception with the problem details every failed <see cref="Result"/> gets
/// (<see cref="HttpResultExtensions.ToProblemDetails"/>): a 500 whose <c>errors</c> hold one
/// <see cref="ErrorType.Failure"/> error coded <see cref="ErrorCode"/>, which a caller's
/// <c>HttpResponseMessage.ToResultAsync</c> reads back as that error. The exception is logged once, at
/// <see cref="LogLevel.Error"/> with its stack, following <see cref="LoggedExceptions"/>: when something else logged it
/// already (the mediator's behaviors do), the handler logs a <see cref="LogLevel.Debug"/> line without the stack. A
/// request the client gave up on (an <see cref="OperationCanceledException"/> once the request is aborted) is a 499,
/// logged at <see cref="LogLevel.Debug"/>: nobody is waiting for an answer.
/// Registered by <c>AddResultProblemDetails()</c>; runs in <c>app.UseExceptionHandler()</c>.
/// </summary>
public sealed partial class ResultExceptionHandler(
    IHostEnvironment environment,
    IOptions<ResultProblemDetailsOptions> options,
    ILogger<ResultExceptionHandler> logger)
    : IExceptionHandler
{
    /// <summary>The code of the error an unhandled exception is answered with.</summary>
    public const string ErrorCode = "Unhandled";

    /// <summary>Its message when the exception isn't shown (outside Development).</summary>
    public const string ErrorMessage = "An unexpected error occurred.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (IsClientAbort(httpContext, exception))
        {
            AnswerClientAbort(httpContext);
            return true;
        }

        LogUnhandledOnce(httpContext, exception);
        // Answered: this journey of the exception ends here, so the same instance thrown in a later request (a cached
        // faulted task) is logged at Error again.
        LoggedExceptions.Release(exception);

        await ProblemFor(exception).ExecuteAsync(httpContext);
        return true;
    }

    private static bool IsClientAbort(
        HttpContext httpContext,
        Exception exception)
        => exception is OperationCanceledException
            && httpContext.RequestAborted.IsCancellationRequested;

    // At Error with the stack, unless whoever saw the exception first (a mediator behavior) logged it so already.
    private void LogUnhandledOnce(
        HttpContext httpContext,
        Exception exception)
    {
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path;
        if (logger.IsEnabled(LogLevel.Error) && LoggedExceptions.Claim(exception))
        {
            LogUnhandled(logger, exception, method, path);
            return;
        }

        if (!logger.IsEnabled(LogLevel.Debug))
            return;

        var exceptionType = exception.GetType().Name;
        LogUnhandledLoggedAlready(logger, exceptionType, method, path);
    }

    private void AnswerClientAbort(
        HttpContext httpContext)
    {
        LogClientAborted(logger, httpContext.Request.Method, httpContext.Request.Path);

        if (!httpContext.Response.HasStarted)
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
    }

    // The same problem details as a failed result, from the same code.
    private IResult ProblemFor(
        Exception exception)
    {
        var showException = ShowsException();
        var error = Error.Failure(
            ErrorCode,
            showException ? $"{exception.GetType().Name}: {exception.Message}" : ErrorMessage);

        var problem = Result.Failure(error).ToProblemDetails();
        if (showException && problem is ProblemHttpResult { ProblemDetails: { } details })
            details.Detail = exception.ToString();

        return problem;
    }

    private bool ShowsException()
        => options.Value.IncludeExceptionDetails ?? environment.IsDevelopment();

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unhandled exception answering {Method} {Path}")]
    private static partial void LogUnhandled(
        ILogger logger,
        Exception exception,
        string method,
        PathString path);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Unhandled {ExceptionType} answering {Method} {Path}, logged where it was thrown")]
    private static partial void LogUnhandledLoggedAlready(
        ILogger logger,
        string exceptionType,
        string method,
        PathString path);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "The client aborted {Method} {Path}")]
    private static partial void LogClientAborted(
        ILogger logger,
        string method,
        PathString path);
}
