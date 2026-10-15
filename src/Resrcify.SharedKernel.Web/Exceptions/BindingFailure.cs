using System;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Web.Exceptions;

/// <summary>
/// What a <see cref="BadHttpRequestException"/> says about the request it refused: the status to answer and one
/// <see cref="ErrorType.Validation"/> error, coded with the parameter's name when ASP.NET Core's message names one. The
/// message is SharedKernel's own: ASP.NET Core's names the parameter's type and may hold the value sent, neither of which
/// belongs in an answer.
/// </summary>
internal static partial class BindingFailure
{
    /// <summary>
    /// The error for <paramref name="exception"/>, and the status to answer it with: the exception's own (400, or 413,
    /// 415, 431 ... for a request too large or of a type nobody reads), or 400 when it holds no client error.
    /// </summary>
    public static (Error Error, int StatusCode) Describe(
        BadHttpRequestException exception)
    {
        var statusCode = exception.StatusCode is >= StatusCodes.Status400BadRequest and < StatusCodes.Status500InternalServerError
            ? exception.StatusCode
            : StatusCodes.Status400BadRequest;

        var parameter = ParameterNamedIn(exception.Message);
        var error = Error.Validation(
            parameter ?? ResultExceptionHandler.BadRequestErrorCode,
            MessageFor(exception.Message, parameter, statusCode));
        return (error, statusCode);
    }

    // ASP.NET Core names the parameter in quotes, as its type and name ("Required parameter "int joinGp" was not provided
    // from query string.", "Failed to bind parameter "Guid id" from "abc".") or by name alone ("Implicit body inferred
    // for parameter "todo" but no body was provided."). The name is the last word: a generic type has spaces too.
    private static string? ParameterNamedIn(string message)
    {
        var match = QuotedParameter().Match(message);
        if (!match.Success)
            return null;

        var declaration = match.Groups["declaration"].Value.Trim();
        var name = declaration[(declaration.LastIndexOf(' ') + 1)..];
        return name.Length > 0 ? name : null;
    }

    private static string MessageFor(
        string message,
        string? parameter,
        int statusCode)
    {
        if (parameter is null)
            return MessageForStatus(statusCode);

        if (message.StartsWith("Required parameter", StringComparison.Ordinal))
            return RequiredFrom().Match(message) is { Success: true } required
                ? $"The required parameter \"{parameter}\" was not provided from {required.Groups["source"].Value}."
                : $"The required parameter \"{parameter}\" was not provided.";

        if (message.StartsWith("Implicit body inferred", StringComparison.Ordinal))
            return $"The request body for parameter \"{parameter}\" was not provided.";

        if (message.StartsWith("Failed to read parameter", StringComparison.Ordinal))
            return $"The parameter \"{parameter}\" couldn't be read from the request body.";

        if (message.StartsWith("Failed to bind parameter", StringComparison.Ordinal))
            return $"The value of parameter \"{parameter}\" is not valid.";

        return $"The parameter \"{parameter}\" couldn't be bound from the request.";
    }

    private static string MessageForStatus(int statusCode)
        => statusCode switch
        {
            StatusCodes.Status413PayloadTooLarge => "The request is too large.",
            StatusCodes.Status415UnsupportedMediaType => "The request's content type isn't supported.",
            StatusCodes.Status431RequestHeaderFieldsTooLarge => "The request's headers are too large.",
            _ => "The request couldn't be read.",
        };

    [GeneratedRegex("parameter \"(?<declaration>[^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex QuotedParameter();

    // "... was not provided from query string." / "from route" / "from header" / "from form" / "from body".
    [GeneratedRegex("was not provided from (?<source>[a-z ]+)\\.?$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex RequiredFrom();
}
