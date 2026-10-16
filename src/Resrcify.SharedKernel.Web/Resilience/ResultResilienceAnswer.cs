using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Results.Serialization;
using Resrcify.SharedKernel.Web.Extensions;

namespace Resrcify.SharedKernel.Web.Resilience;

/// <summary>
/// The response <c>AddResultResilience</c> answers with in the service's place when the call timed out, the circuit is
/// open or the service couldn't be reached (see <see cref="ResultResilienceErrors"/>): problem details as SharedKernel
/// writes them for the error's type, so <c>ToResultAsync</c> reads the error back as it is.
/// </summary>
internal static class ResultResilienceAnswer
{
    private const string ProblemJson = "application/problem+json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ErrorTypeJsonConverter() },
    };

    /// <summary>The answer in place of <paramref name="exception"/>: a timeout, an open circuit or no answer at all.</summary>
    public static HttpResponseMessage For(
        Exception exception,
        HttpRequestMessage? request)
    {
        var (error, marker) = exception switch
        {
            BrokenCircuitException => (ResultResilienceErrors.CircuitOpen, ResultResilienceErrors.CircuitOpenMarker),
            HttpRequestException unreachable => (Unreachable(unreachable, request), ResultResilienceErrors.UnreachableMarker),
            _ => (ResultResilienceErrors.Timeout, ResultResilienceErrors.TimeoutMarker),
        };

        var status = ErrorTypes.ToHttpStatus(error.Type);
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            RequestMessage = request,
            ReasonPhrase = HttpResultExtensions.GetTitle(error.Type),
            Content = new StringContent(
                ProblemDetails(error, status),
                Encoding.UTF8,
                ProblemJson),
        };
        response.Headers.Add(ResultResilienceErrors.Header, marker);
        return response;
    }

    // Named by the authority called (its host and port, never the path or query, which may hold data) and the cause.
    private static Error Unreachable(
        HttpRequestException exception,
        HttpRequestMessage? request)
        => ResultResilienceErrors.Unreachable(
            request?.RequestUri is { IsAbsoluteUri: true } uri
                ? uri.Authority
                : "an unknown address",
            exception.Message);

    private static string ProblemDetails(
        Error error,
        int status)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = HttpResultExtensions.GetTitle(error.Type),
            Type = HttpResultExtensions.GetType(error.Type),
            Detail = error.Message,
        };
        problem.Extensions["errors"] = new[] { error };
        return JsonSerializer.Serialize(problem, JsonOptions);
    }
}
