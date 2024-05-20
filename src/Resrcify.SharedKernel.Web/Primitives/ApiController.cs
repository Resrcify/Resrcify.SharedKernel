using System;
using System.Collections.Generic;
using System.Linq;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resrcify.SharedKernel.ResultFramework.Shared;
using ResultExtensions = Resrcify.SharedKernel.Web.Extensions.ResultExtensions;

namespace Resrcify.SharedKernel.Web.Primitives;

[ApiController]
public abstract class ApiController : ControllerBase
{
    protected readonly ISender _sender;

    protected ApiController(ISender sender)
        => _sender = sender;

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
                { nameof(result.Errors), result.Errors }
            });
    }
}