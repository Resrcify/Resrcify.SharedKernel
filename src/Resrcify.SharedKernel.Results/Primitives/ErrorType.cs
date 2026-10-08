namespace Resrcify.SharedKernel.Results.Primitives;

public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
    Timeout = 6,
    RateLimit = 7,
    ExternalFailure = 8,

    /// <summary>
    /// The request is well formed but can't be processed as sent (HTTP 422): it breaks a rule of the operation, e.g. an
    /// idempotency key already used for a different request. About the request itself, so not transient.
    /// </summary>
    Unprocessable = 9,
}
