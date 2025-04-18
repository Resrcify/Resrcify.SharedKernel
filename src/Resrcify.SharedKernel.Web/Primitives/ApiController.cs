using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using ResultExtensions = Resrcify.SharedKernel.Web.Extensions.ResultExtensions;

namespace Resrcify.SharedKernel.Web.Primitives;

[ApiController]
public abstract class ApiController : ControllerBase
{
    protected ISender Sender { get; }

    protected ApiController(ISender sender)
        => Sender = sender;

    [SuppressMessage(
    "Globalization",
    "CA1308:Normalize strings to uppercase",
    Justification = "Lowercase needed for consistent JSON keys")]
    public static IResult ToProblemDetails(Result result)
    {
        if (result.IsSuccess)
            throw new InvalidOperationException("Successful result should not be converted to problem details.");

        var firstError = result.Errors.FirstOrDefault();
        var errorType = firstError?.Type ?? ErrorType.Failure;

        return Results.Problem(
            statusCode: ResultExtensions.GetStatusCode(errorType),
            title: ResultExtensions.GetTitle(errorType),
            type: ResultExtensions.GetType(errorType),
            extensions: new Dictionary<string, object?>
            {
                { nameof(result.Errors).ToLowerInvariant(), result.Errors }
            });
    }
}