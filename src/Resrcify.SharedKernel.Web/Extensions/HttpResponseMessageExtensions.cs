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

/// <summary>
/// Reads an <see cref="HttpResponseMessage"/> as a <see cref="Result"/>: the body on success, the problem details'
/// errors (or one error for the status) on failure.
/// </summary>
public static class HttpResponseMessageExtensions
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<Result<T>> ToResultAsync<T>(
        this HttpResponseMessage response,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
            return Result.Failure<T>(await ReadErrorsAsync(response, options, cancellationToken));

        if (HasNoContent(response))
            return Result.Failure<T>(EmptyContent<T>(response));

        T? value;
        try
        {
            await using var content = await response.Content.ReadAsStreamAsync(
                cancellationToken);

            value = await JsonSerializer.DeserializeAsync<T>(
                content,
                options ?? _options,
                cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            return Result.Failure<T>(new Error(
                "Http.UnreadableContent",
                $"The response ({(int)response.StatusCode}) could not be read as a {typeof(T).Name}: {exception.Message}",
                ErrorType.ExternalFailure));
        }

        return value is null
            ? Result.Failure<T>(EmptyContent<T>(response))
            : Result.Success(value);
    }

    public static async Task<Result> ToResultAsync(
        this HttpResponseMessage response,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
            return Result.Success();

        return Result.Failure(await ReadErrorsAsync(response, options, cancellationToken));
    }

    // The errors of a failed response: those in its problem details, or else one error for its status,
    // whatever the body is (empty, an HTML error page from a proxy, problem details without errors).
    private static async Task<Error[]> ReadErrorsAsync(
        HttpResponseMessage response,
        JsonSerializerOptions? options,
        CancellationToken cancellationToken)
    {
        if (HasNoContent(response))
            return [StatusError(response)];

        try
        {
            await using var content = await response.Content.ReadAsStreamAsync(
                cancellationToken);

            var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(
                content,
                options ?? _options,
                cancellationToken: cancellationToken);

            return TryExtractErrors(problemDetails, options, out var errors)
                ? errors
                : [StatusError(response)];
        }
        catch (JsonException)
        {
            return [StatusError(response)];
        }
    }

    private static bool HasNoContent(HttpResponseMessage response)
        => response.StatusCode == System.Net.HttpStatusCode.NoContent
            || response.Content.Headers.ContentLength == 0;

    private static Error StatusError(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        return new Error(
            $"Http.{statusCode}",
            response.ReasonPhrase ?? response.StatusCode.ToString(),
            HttpResultExtensions.GetErrorType(statusCode));
    }

    private static Error EmptyContent<T>(HttpResponseMessage response)
        => new(
            "Http.EmptyContent",
            $"The response ({(int)response.StatusCode}) had no {typeof(T).Name} in it.",
            ErrorType.ExternalFailure);

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
