using System;
using System.Collections.Immutable;
using System.Globalization;
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
using Resrcify.SharedKernel.Abstractions.Resilience;
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
/// Any other exception the container's <see cref="ITransientFailureClassifier"/> calls transient (the one the outbox,
/// the jobs and the mediator's behaviors judge by: a transient database failure, a timeout or a handler's own
/// cancellation, an HTTP call without an answer or with a 408, 429 or 5xx, a <see cref="TransientFailureException"/>
/// without errors, or a registered rule's) is answered 503 with a <c>Retry-After</c>
/// (<see cref="ResultProblemDetailsOptions.TransientFailureRetryAfter"/>), its one <see cref="ErrorType.Failure"/> error
/// coded <see cref="ErrorCodes.Request.Unavailable"/>, logged at <see cref="LogLevel.Warning"/>, once: another try may
/// get past it, which a 500 doesn't say.
/// Registered by <c>AddResultProblemDetails()</c>; runs in <c>app.UseExceptionHandler()</c>.
/// </summary>
/// <param name="environment">The host's environment (whether the exception is shown).</param>
/// <param name="options">How the exception is answered.</param>
/// <param name="logger">The handler's logger.</param>
/// <param name="classifier">
/// Which exceptions are transient; <c>AddResultProblemDetails()</c> registers the default one. Without it, SharedKernel's
/// built-in rule (<see cref="TransientFailures.IsTransient(Exception)"/>).
/// </param>
public sealed partial class ResultExceptionHandler(
    IHostEnvironment environment,
    IOptions<ResultProblemDetailsOptions> options,
    ILogger<ResultExceptionHandler> logger,
    ITransientFailureClassifier? classifier = null)
    : IExceptionHandler
{
    /// <summary>Its message when the exception isn't shown (outside Development).</summary>
    public const string ErrorMessage = "An unexpected error occurred.";

    /// <summary>Its message for a transient exception when the exception isn't shown (outside Development).</summary>
    public const string TransientErrorMessage = "The service couldn't answer just now; try again.";

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

        if (IsTransient(exception))
        {
            await AnswerUnavailableAsync(httpContext, exception);
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

    private bool IsTransient(Exception exception)
        => classifier?.IsTransient(exception) ?? TransientFailures.IsTransient(exception);

    // A 503 with Retry-After: the service couldn't answer this time, and another try may get past it.
    private async Task AnswerUnavailableAsync(
        HttpContext httpContext,
        Exception exception)
    {
        LogUnavailableOnce(httpContext, exception);
        LoggedExceptions.Release(exception);

        if (httpContext.Response.HasStarted)
            return;

        var problem = ProblemFor(exception, ErrorCodes.Request.Unavailable, TransientErrorMessage);
        if (problem is ProblemHttpResult { ProblemDetails: { } details })
            SetStatus(details, StatusCodes.Status503ServiceUnavailable);

        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds().ToString(CultureInfo.InvariantCulture);
        await problem.ExecuteAsync(httpContext);
    }

    private long RetryAfterSeconds()
    {
        var retryAfter = options.Value.TransientFailureRetryAfter;
        return retryAfter <= TimeSpan.Zero
            ? 0
            : (long)Math.Ceiling(retryAfter.TotalSeconds);
    }

    // At Warning with the stack, unless whoever saw the exception first (a mediator behavior) logged it so already.
    private void LogUnavailableOnce(
        HttpContext httpContext,
        Exception exception)
    {
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path;
        if (logger.IsEnabled(LogLevel.Warning) && LoggedExceptions.Claim(exception))
        {
            LogUnavailable(logger, exception, method, path);
            return;
        }

        if (!logger.IsEnabled(LogLevel.Debug))
            return;

        var exceptionType = exception.GetType().Name;
        LogUnavailableLoggedAlready(logger, exceptionType, method, path);
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
            SetStatus(details, statusCode);

        if (ShowsException())
            details.Detail = exception.Message;
    }

    private static void SetStatus(
        ProblemDetails details,
        int statusCode)
    {
        details.Status = statusCode;
        details.Title = ReasonPhrases.GetReasonPhrase(statusCode);
        details.Type = $"https://www.rfc-editor.org/rfc/rfc9110#status.{statusCode}";
    }

    // The same problem details as a failed result, from the same code.
    private IResult ProblemFor(
        Exception exception,
        string code = ErrorCodes.Request.Unhandled,
        string message = ErrorMessage)
    {
        var showException = ShowsException();
        var error = Error.Failure(
            code,
            showException ? $"{exception.GetType().Name}: {exception.Message}" : message);

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
        Message = "{Method} {Path} failed transiently, answered 503")]
    private static partial void LogUnavailable(
        ILogger logger,
        Exception exception,
        string method,
        PathString path);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "{Method} {Path} failed transiently ({ExceptionType}), answered 503 (logged where it was thrown)")]
    private static partial void LogUnavailableLoggedAlready(
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
