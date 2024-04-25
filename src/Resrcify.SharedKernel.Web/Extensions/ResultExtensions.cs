using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Resrcify.SharedKernel.ResultFramework.Shared;

namespace Resrcify.SharedKernel.Web.Shared;

public static class ResultExtensions
{
    public static IResult ToProblemDetails(this Result result)
    {
        var firstError = result.Errors.FirstOrDefault();
        var errorType = firstError?.Type ?? ErrorType.Failure;

        return result switch
        {
            { IsSuccess: true } => throw new InvalidOperationException(),
            _ => Results.Problem(
                statusCode: GetStatusCode(errorType),
                title: GetTitle(errorType),
                type: GetType(errorType),
                extensions: new Dictionary<string, object?>
                {
                    { nameof(result.Errors), result.Errors }
                })
        };
    }

    internal static string GetType(ErrorType errorType)
        => errorType switch
        {
            ErrorType.Validation => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1",
            ErrorType.NotFound => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.4",
            ErrorType.Conflict => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8",
            _ => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1"
        };

    internal static string GetTitle(ErrorType errorType)
        => errorType switch
        {
            ErrorType.Validation => "Bad Request",
            ErrorType.NotFound => "Not Found",
            ErrorType.Conflict => "Conflict",
            _ => "Internal Server Error"
        };
    internal static int GetStatusCode(ErrorType errorType)
        => errorType switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

    public static async Task<IResult> Match(
        this Task<Result> resultTask,
        Func<IResult> onSuccess,
        Func<Result, IResult> onFailure
    )
    {
        var result = await resultTask;
        return result.IsSuccess ? onSuccess() : onFailure(result);
    }

    public static async Task<IResult> Match<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, IResult> onSuccess,
        Func<Result, IResult> onFailure
    )
    {
        var result = await resultTask;
        return result.IsSuccess ? onSuccess(result.Value) : onFailure(result);
    }
}