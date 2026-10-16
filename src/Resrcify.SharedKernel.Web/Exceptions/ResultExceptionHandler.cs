using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
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
/// <see cref="ErrorType.Failure"/> error coded <see cref="ErrorCodes.Request.Unhandled"/>, which a caller's
/// <c>HttpResponseMessage.ToResultAsync</c> reads back as that error. The exception is logged once, at
/// <see cref="LogLevel.Error"/> with its stack, following <see cref="LoggedExceptions"/>: when something else logged it
/// already (the mediator's behaviors do), the handler logs a <see cref="LogLevel.Debug"/> line without the stack. A
/// request the client gave up on never gets here: <c>UseExceptionHandler()</c> answers an
/// <see cref="OperationCanceledException"/> or <see cref="System.IO.IOException"/> after the request was aborted with a
/// 499 itself (logged at <see cref="LogLevel.Debug"/>), before it asks any handler.
/// A request ASP.NET Core couldn't bind (a <see cref="BadHttpRequestException"/>: a required parameter left out, a
/// value that doesn't parse, a body that isn't valid JSON) isn't a fault of the service: it is answered with the
/// exception's status (400, or 413, 415 ...) and problem details holding one <see cref="ErrorType.Validation"/> error
/// coded with the parameter's name (<see cref="ErrorCodes.Request.Binding"/> when the exception names none), logged at
/// <see cref="LogLevel.Debug"/>. Its message names only the parameter, never its type or the value sent; in Development
/// (<see cref="ResultProblemDetailsOptions.IncludeExceptionDetails"/>) the problem's <c>detail</c> holds ASP.NET Core's
/// own message. <c>AddResultProblemDetails()</c> has minimal APIs throw it in every environment
/// (<see cref="ResultProblemDetailsOptions.ProblemDetailsForBindingFailures"/>).
/// A <see cref="TransientFailureException"/> (itself or among the exception's causes) holding errors is answered as the
/// failed result it stands for: problem details of its <see cref="TransientFailureException.Errors"/> (a 502 for an
/// upstream's failure, a 504 for a timeout ...), logged at <see cref="LogLevel.Warning"/>, once.
/// Registered by <c>AddResultProblemDetails()</c>; runs in <c>app.UseExceptionHandler()</c>.
/// </summary>
public sealed partial class ResultExceptionHandler(
    IHostEnvironment environment,
    IOptions<ResultProblemDetailsOptions> options,
    ILogger<ResultExceptionHandler> logger)
    : IExceptionHandler
{
    /// <summary>Its message when the exception isn't shown (outside Development).</summary>
    public const string ErrorMessage = "An unexpected error occurred.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is BadHttpRequestException badRequest)
        {
            await AnswerBindingFailureAsync(httpContext, badRequest);
            return true;
        }

        if (TransientFailures.TryGetErrors(exception, out var errors))
        {
            await AnswerTransientFailureAsync(httpContext, exception, errors);
            return true;
        }

        LogUnhandledOnce(httpContext, exception);
        // Answered: this journey of the exception ends here, so the same instance thrown in a later request (a cached
        // faulted task) is logged at Error again.
        LoggedExceptions.Release(exception);

        await ProblemFor(exception).ExecuteAsync(httpContext);
        return true;
    }

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

    // The failed result the exception stands for, as a handler returning it would have been answered.
    private async Task AnswerTransientFailureAsync(
        HttpContext httpContext,
        Exception exception,
        ImmutableArray<Error> errors)
    {
        LogTransientFailureOnce(httpContext, exception, errors);
        LoggedExceptions.Release(exception);

        if (httpContext.Response.HasStarted)
            return;

        await Result.Failure(errors).ToProblemDetails().ExecuteAsync(httpContext);
    }

    // At Warning with the stack, unless whoever saw the exception first (a mediator behavior) logged it so already.
    private void LogTransientFailureOnce(
        HttpContext httpContext,
        Exception exception,
        ImmutableArray<Error> errors)
    {
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path;
        if (logger.IsEnabled(LogLevel.Warning) && LoggedExceptions.Claim(exception))
        {
            LogTransientFailure(logger, exception, method, path, Describe(errors));
            return;
        }

        if (!logger.IsEnabled(LogLevel.Debug))
            return;

        var described = Describe(errors);
        LogTransientFailureLoggedAlready(logger, method, path, described);
    }

    private static string Describe(ImmutableArray<Error> errors)
        => string.Join("; ", errors.Select(error => error.ToString()));

    // The problem details of a failed validation, with the exception's own status (a 413 or a 415 stays one).
    private async Task AnswerBindingFailureAsync(
        HttpContext httpContext,
        BadHttpRequestException exception)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogBindingFailed(
                logger,
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception.StatusCode,
                exception.Message);
        }

        if (httpContext.Response.HasStarted)
            return;

        var (error, statusCode) = BindingFailure.Describe(exception);
        var problem = Result.Failure(error).ToProblemDetails();
        if (problem is ProblemHttpResult { ProblemDetails: { } details })
            Adjust(details, statusCode, exception);

        await problem.ExecuteAsync(httpContext);
    }

    private void Adjust(
        ProblemDetails details,
        int statusCode,
        BadHttpRequestException exception)
    {
        if (statusCode != StatusCodes.Status400BadRequest)
        {
            details.Status = statusCode;
            details.Title = ReasonPhrases.GetReasonPhrase(statusCode);
            details.Type = $"https://www.rfc-editor.org/rfc/rfc9110#status.{statusCode}";
        }

        if (ShowsException())
            details.Detail = exception.Message;
    }

    // The same problem details as a failed result, from the same code.
    private IResult ProblemFor(
        Exception exception)
    {
        var showException = ShowsException();
        var error = Error.Failure(
            ErrorCodes.Request.Unhandled,
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
        Level = LogLevel.Warning,
        Message = "{Method} {Path} failed transiently, answered with its errors: {Errors}")]
    private static partial void LogTransientFailure(
        ILogger logger,
        Exception exception,
        string method,
        PathString path,
        string errors);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "{Method} {Path} failed transiently, answered with its errors: {Errors} (logged where it was thrown)")]
    private static partial void LogTransientFailureLoggedAlready(
        ILogger logger,
        string method,
        PathString path,
        string errors);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "{Method} {Path} couldn't be bound ({StatusCode}): {Reason}")]
    private static partial void LogBindingFailed(
        ILogger logger,
        string method,
        PathString path,
        int statusCode,
        string reason);
}
