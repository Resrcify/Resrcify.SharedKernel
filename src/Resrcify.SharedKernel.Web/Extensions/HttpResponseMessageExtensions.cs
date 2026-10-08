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
using Resrcify.SharedKernel.Results.Serialization;

namespace Resrcify.SharedKernel.Web.Extensions;

/// <summary>
/// Reads an <see cref="HttpResponseMessage"/> as a <see cref="Result"/>: the body on success, the problem details'
/// errors (or one error for the status) on failure. The options given apply to the body; the problem details and
/// their errors are read as SharedKernel writes them, whatever the options.
/// </summary>
public static class HttpResponseMessageExtensions
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        // An error type this version doesn't know (a newer service's) reads as a Failure instead of failing the read.
        Converters = { new ErrorTypeJsonConverter(), new JsonStringEnumConverter() }
    };

    public static async Task<Result<T>> ToResultAsync<T>(
        this HttpResponseMessage response,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
            return Result.Failure<T>(await ReadErrorsAsync(response, cancellationToken));

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
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
            return Result.Success();

        return Result.Failure(await ReadErrorsAsync(response, cancellationToken));
    }

    // The errors of a failed response: those in its problem details, or else one error for its status,
    // whatever the body is (empty, an HTML error page from a proxy, problem details without errors). Read with this
    // class's options, not the caller's: those are chosen for the success body and could misread the errors.
    private static async Task<Error[]> ReadErrorsAsync(
        HttpResponseMessage response,
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
                _options,
                cancellationToken: cancellationToken);

            return TryExtractErrors(problemDetails, out var errors)
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

    // A null entry or Error.None (as a 3.x server wrote Result.Failure([Error.None])) is no error: dropped, and with
    // nothing left the status's own error stands in.
    private static bool TryExtractErrors(
        ProblemDetails? details,
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

        if (json.ValueKind == JsonValueKind.Array)
        {
            errors = (json.Deserialize<Error?[]>(_options) ?? [])
                .OfType<Error>()
                .Where(error => error != Error.None)
                .ToArray();
            return errors.Length > 0;
        }

        if (json.ValueKind == JsonValueKind.Object)
        {
            var dict = json.Deserialize<Dictionary<string, string?[]?>>(_options) ?? [];
            errors = dict
                .Where(kvp => kvp.Value is not null)
                .SelectMany(
                    kvp => kvp.Value!.OfType<string>().Select(
                        v => Error.Validation(
                            kvp.Key,
                            v)))
                .ToArray();
            return errors.Length > 0;
        }

        return false;
    }
}
