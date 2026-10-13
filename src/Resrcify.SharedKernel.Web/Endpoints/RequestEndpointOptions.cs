using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Extensions;

namespace Resrcify.SharedKernel.Web.Endpoints;

/// <summary>
/// How the request endpoints (<c>MapGetRequest</c>, <c>MapPostRequest</c>, …) answer a failed request, for the whole app:
/// <c>services.Configure&lt;RequestEndpointOptions&gt;(options =&gt; ...)</c>. Read when the endpoints are mapped.
/// </summary>
public sealed class RequestEndpointOptions
{
    /// <summary>The answer to a failed result; problem details by <c>ErrorType</c> unless changed.</summary>
    public Func<Result, IResult> OnFailure { get; set; } = HttpResultExtensions.ToProblemDetails;

    /// <summary>The problem statuses each endpoint declares for OpenAPI (add an endpoint's own with <c>ProducesProblem</c>).</summary>
    public IList<int> ProblemStatusCodes { get; } =
    [
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status500InternalServerError,
    ];
}
