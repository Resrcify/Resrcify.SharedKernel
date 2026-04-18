using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class ResultExtensions
{
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Lowercase needed for consistent JSON keys")]
    public static IResult ToProblemDetails(
        this Result result)
    {
        var errors = result.Errors.ToArray();

        if (errors.Length == 0)
            errors = [Error.None];

        var dominantType = GetDominantErrorType(
            errors);

        return result switch
        {
            { IsSuccess: true } => throw new InvalidOperationException(
                "Successful result should not be converted to problem details."),
            _ => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: GetStatusCode(dominantType),
                title: GetTitle(dominantType),
                type: GetType(dominantType),
                extensions: new Dictionary<string, object?>
                {
                    { nameof(result.Errors).ToLowerInvariant(), result.Errors }
                })
        };
    }
    private static readonly ErrorType[] severityOrder =
    [
        ErrorType.Failure,
        ErrorType.ExternalFailure,
        ErrorType.Timeout,
        ErrorType.Forbidden,
        ErrorType.Unauthorized,
        ErrorType.Conflict,
        ErrorType.Validation,
        ErrorType.NotFound,
        ErrorType.RateLimit
    ];
    private static ErrorType GetDominantErrorType(
        Error[] errors)
        => errors
            .Select(e => e.Type)
            .OrderBy(t => Array.IndexOf(severityOrder, t))
            .FirstOrDefault(ErrorType.Failure);

    internal static string GetType(ErrorType errorType)
        => errorType switch
        {
            ErrorType.Validation => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1",
            ErrorType.Unauthorized => "https://datatracker.ietf.org/doc/html/rfc7235#section-3.1",
            ErrorType.Forbidden => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.3",
            ErrorType.NotFound => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.4",
            ErrorType.Conflict => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8",
            ErrorType.Timeout => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.5",
            ErrorType.RateLimit => "https://datatracker.ietf.org/doc/html/rfc6585#section-4",
            ErrorType.ExternalFailure => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.3",
            ErrorType.Failure => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
            _ => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1"
        };

    internal static string GetTitle(
        ErrorType errorType)
        => errorType switch
        {
            ErrorType.Validation => "Bad Request",
            ErrorType.Unauthorized => "Unauthorized",
            ErrorType.Forbidden => "Forbidden",
            ErrorType.NotFound => "Not Found",
            ErrorType.Conflict => "Conflict",
            ErrorType.Timeout => "Request Timeout",
            ErrorType.RateLimit => "Too Many Requests",
            ErrorType.ExternalFailure => "Bad Gateway",
            ErrorType.Failure => "Internal Server Error",
            _ => "Internal Server Error"
        };
    internal static int GetStatusCode(ErrorType errorType)
           => errorType switch
           {
               ErrorType.Validation => StatusCodes.Status400BadRequest,
               ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
               ErrorType.Forbidden => StatusCodes.Status403Forbidden,
               ErrorType.NotFound => StatusCodes.Status404NotFound,
               ErrorType.Conflict => StatusCodes.Status409Conflict,
               ErrorType.Timeout => StatusCodes.Status504GatewayTimeout,
               ErrorType.RateLimit => StatusCodes.Status429TooManyRequests,
               ErrorType.ExternalFailure => StatusCodes.Status502BadGateway,
               ErrorType.Failure => StatusCodes.Status500InternalServerError,
               _ => StatusCodes.Status500InternalServerError
           };

    internal static ErrorType GetErrorType(int statusCode)
        => statusCode switch
        {
            StatusCodes.Status400BadRequest => ErrorType.Validation,
            StatusCodes.Status401Unauthorized => ErrorType.Unauthorized,
            StatusCodes.Status403Forbidden => ErrorType.Forbidden,
            StatusCodes.Status404NotFound => ErrorType.NotFound,
            StatusCodes.Status409Conflict => ErrorType.Conflict,
            StatusCodes.Status504GatewayTimeout => ErrorType.Timeout,
            StatusCodes.Status429TooManyRequests => ErrorType.RateLimit,
            StatusCodes.Status502BadGateway => ErrorType.ExternalFailure,
            _ => ErrorType.Failure
        };

    public static async Task<IResult> Match(
        this Task<Result> resultTask,
        Func<IResult> onSuccess,
        Func<Result, IResult> onFailure)
    {
        var result = await resultTask;
        return result.IsSuccess
            ? onSuccess()
            : onFailure(result);
    }

    public static async Task<IResult> Match<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, IResult> onSuccess,
        Func<Result, IResult> onFailure)
    {
        var result = await resultTask;
        return result.IsSuccess
            ? onSuccess(result.Value)
            : onFailure(result);
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
        await using var content = await response.Content.ReadAsStreamAsync(
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var result = await JsonSerializer.DeserializeAsync<T>(
                content,
                options ?? _options,
                cancellationToken: cancellationToken);

            return result is null
                ? Error.None
                : Result.Success(result);
        }

        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            content,
            options ?? _options,
            cancellationToken: cancellationToken);

        if (!TryExtractErrors(
            problemDetails,
            options,
            out var errors))
            return Result.Failure<T>([Error.None]);

        return Result.Failure<T>(errors);
    }
    public static async Task<Result> Convert(
        this HttpResponseMessage response,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
            return Result.Success();

        await using var content = await response.Content.ReadAsStreamAsync(
            cancellationToken);

        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            content,
            options ?? _options,
            cancellationToken: cancellationToken);

        if (!TryExtractErrors(
            problemDetails,
            options,
            out var errors))
            return Result.Failure([Error.None]);

        return Result.Failure(
            errors);
    }
    private static bool TryExtractErrors(
        ProblemDetails? details,
        JsonSerializerOptions? options,
        out Error[] errors)
    {
        errors = [];
        if (details?.Extensions is null)
            return false;

        if (!details.Extensions.TryGetValue(
            "errors",
            out var errorsObj))
            return false;

        if (errorsObj is not JsonElement json)
            return false;

        var opts = options ?? _options;

        if (json.ValueKind == JsonValueKind.Array)
        {
            errors = json.Deserialize<Error[]>(opts) ?? [];
            return errors.Length > 0;
        }

        if (json.ValueKind == JsonValueKind.Object)
        {
            var dict = json.Deserialize<Dictionary<string, string[]>>(opts) ?? [];
            errors = dict
                .SelectMany(
                    kvp => kvp.Value.Select(
                        v => Error.Validation(
                            kvp.Key,
                            v)))
                .ToArray();
            return errors.Length > 0;
        }

        return false;
    }
}