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
/// The response <c>AddResultResilience</c> answers with in the service's place when the call timed out or the circuit
/// is open (see <see cref="ResultResilienceErrors"/>): problem details as SharedKernel writes them for the error's type,
/// so <c>ToResultAsync</c> reads the error back as it is.
/// </summary>
internal static class ResultResilienceAnswer
{
    private const string ProblemJson = "application/problem+json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ErrorTypeJsonConverter() },
    };

    /// <summary>The answer in place of <paramref name="exception"/>, a timeout or an open circuit.</summary>
    public static HttpResponseMessage For(
        Exception exception,
        HttpRequestMessage? request)
    {
        var (error, marker) = exception is BrokenCircuitException
            ? (ResultResilienceErrors.CircuitOpen, ResultResilienceErrors.CircuitOpenMarker)
            : (ResultResilienceErrors.Timeout, ResultResilienceErrors.TimeoutMarker);

        var status = HttpResultExtensions.GetStatusCode(error.Type);
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
