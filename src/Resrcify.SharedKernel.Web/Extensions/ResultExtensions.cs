using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resrcify.SharedKernel.ResultFramework.Primitives;

namespace Resrcify.SharedKernel.Web.Extensions;

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

    internal static ErrorType GetErrorType(int statusCode)
        => statusCode switch
        {
            StatusCodes.Status400BadRequest => ErrorType.Validation,
            StatusCodes.Status404NotFound => ErrorType.NotFound,
            StatusCodes.Status409Conflict => ErrorType.Conflict,
            _ => ErrorType.Failure
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

    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<Result<T>> Convert<T>(
        this HttpResponseMessage response,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
            return await JsonSerializer.DeserializeAsync<T>(
                content,
                options ?? _options,
                cancellationToken: cancellationToken);

        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            content,
            options ?? _options,
            cancellationToken: cancellationToken);

        if (problemDetails?.Extensions.TryGetValue("Errors", out var errorsObject) == true &&
            errorsObject is JsonElement jsonElement &&
            jsonElement.ValueKind == JsonValueKind.Array)
        {
            var errors = jsonElement.Deserialize<Error[]>(options ?? _options);
            return Result.Failure<T>(errors ?? [Error.None]);
        }

        return Result.Failure<T>([Error.None]);
    }
    public static async Task<Result> Convert(
        this HttpResponseMessage response,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
            return Result.Success();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);

        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            content,
            options ?? _options,
            cancellationToken: cancellationToken);

        if (problemDetails?.Extensions.TryGetValue("Errors", out var errorsObject) == true &&
            errorsObject is JsonElement jsonElement &&
            jsonElement.ValueKind == JsonValueKind.Array)
        {
            var errors = jsonElement.Deserialize<Error[]>(options ?? _options);
            return Result.Failure(errors ?? [Error.None]);
        }

        return Result.Failure([Error.None]);
    }
}