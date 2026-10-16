using System.Net;

namespace Resrcify.SharedKernel.Results.Primitives;

/// <summary>
/// The one mapping between an <see cref="ErrorType"/> and an HTTP status, in both directions: the Web package answers a
/// failed result with <see cref="ToHttpStatus"/>, reads a failed response back with <see cref="FromHttpStatus"/>, and
/// the transient-failure rule (<c>TransientFailures</c>) judges an <c>HttpRequestException</c>'s status by it.
/// </summary>
public static class ErrorTypes
{
    /// <summary>
    /// The error type a status stands for: 400 <see cref="ErrorType.Validation"/>, 401 <see cref="ErrorType.Unauthorized"/>,
    /// 403 <see cref="ErrorType.Forbidden"/>, 404 and 410 <see cref="ErrorType.NotFound"/>, 409 and 412
    /// <see cref="ErrorType.Conflict"/>, 422 <see cref="ErrorType.Unprocessable"/>, 408 and 504
    /// <see cref="ErrorType.Timeout"/>, 429 <see cref="ErrorType.RateLimit"/>, 502 <see cref="ErrorType.ExternalFailure"/>.
    /// Any other 4xx is about the request itself (<see cref="ErrorType.Validation"/>, not transient); any other status is a
    /// <see cref="ErrorType.Failure"/>.
    /// </summary>
    /// <param name="statusCode">The HTTP status.</param>
    public static ErrorType FromHttpStatus(int statusCode)
        => statusCode switch
        {
            (int)HttpStatusCode.BadRequest => ErrorType.Validation,
            (int)HttpStatusCode.Unauthorized => ErrorType.Unauthorized,
            (int)HttpStatusCode.Forbidden => ErrorType.Forbidden,
            (int)HttpStatusCode.NotFound => ErrorType.NotFound,
            (int)HttpStatusCode.Gone => ErrorType.NotFound,
            (int)HttpStatusCode.Conflict => ErrorType.Conflict,
            (int)HttpStatusCode.PreconditionFailed => ErrorType.Conflict,
            (int)HttpStatusCode.UnprocessableEntity => ErrorType.Unprocessable,
            (int)HttpStatusCode.RequestTimeout => ErrorType.Timeout,
            (int)HttpStatusCode.GatewayTimeout => ErrorType.Timeout,
            (int)HttpStatusCode.TooManyRequests => ErrorType.RateLimit,
            (int)HttpStatusCode.BadGateway => ErrorType.ExternalFailure,
            >= 400 and < 500 => ErrorType.Validation,
            _ => ErrorType.Failure,
        };

    /// <summary>
    /// The status a failed result of this type is answered with: <see cref="ErrorType.Validation"/> 400,
    /// <see cref="ErrorType.Unauthorized"/> 401, <see cref="ErrorType.Forbidden"/> 403, <see cref="ErrorType.NotFound"/>
    /// 404, <see cref="ErrorType.Conflict"/> 409, <see cref="ErrorType.Unprocessable"/> 422,
    /// <see cref="ErrorType.Timeout"/> 504, <see cref="ErrorType.RateLimit"/> 429, <see cref="ErrorType.ExternalFailure"/>
    /// 502, and <see cref="ErrorType.Failure"/> (or a type this version doesn't know) 500.
    /// </summary>
    /// <param name="type">The error type.</param>
    public static int ToHttpStatus(ErrorType type)
        => type switch
        {
            ErrorType.Validation => (int)HttpStatusCode.BadRequest,
            ErrorType.Unauthorized => (int)HttpStatusCode.Unauthorized,
            ErrorType.Forbidden => (int)HttpStatusCode.Forbidden,
            ErrorType.NotFound => (int)HttpStatusCode.NotFound,
            ErrorType.Conflict => (int)HttpStatusCode.Conflict,
            ErrorType.Unprocessable => (int)HttpStatusCode.UnprocessableEntity,
            ErrorType.Timeout => (int)HttpStatusCode.GatewayTimeout,
            ErrorType.RateLimit => (int)HttpStatusCode.TooManyRequests,
            ErrorType.ExternalFailure => (int)HttpStatusCode.BadGateway,
            _ => (int)HttpStatusCode.InternalServerError,
        };
}
