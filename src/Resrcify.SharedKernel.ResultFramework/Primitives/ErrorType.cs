namespace Resrcify.SharedKernel.ResultFramework.Primitives;

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
}