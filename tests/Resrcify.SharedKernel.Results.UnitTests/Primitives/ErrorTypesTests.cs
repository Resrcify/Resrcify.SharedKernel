using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Results.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ErrorTypesTests
{
    [Theory]
    [InlineData(400, ErrorType.Validation)]
    [InlineData(401, ErrorType.Unauthorized)]
    [InlineData(403, ErrorType.Forbidden)]
    [InlineData(404, ErrorType.NotFound)]
    [InlineData(410, ErrorType.NotFound)]
    [InlineData(409, ErrorType.Conflict)]
    [InlineData(412, ErrorType.Conflict)]
    [InlineData(422, ErrorType.Unprocessable)]
    [InlineData(408, ErrorType.Timeout)]
    [InlineData(504, ErrorType.Timeout)]
    [InlineData(429, ErrorType.RateLimit)]
    [InlineData(502, ErrorType.ExternalFailure)]
    [InlineData(418, ErrorType.Validation)]
    [InlineData(500, ErrorType.Failure)]
    [InlineData(503, ErrorType.Failure)]
    [InlineData(200, ErrorType.Failure)]
    public void FromHttpStatus_ShouldGiveTheErrorTypeTheStatusStandsFor(int status, ErrorType type)
        => ErrorTypes.FromHttpStatus(status).ShouldBe(type);

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Unprocessable, 422)]
    [InlineData(ErrorType.Timeout, 504)]
    [InlineData(ErrorType.RateLimit, 429)]
    [InlineData(ErrorType.ExternalFailure, 502)]
    [InlineData(ErrorType.Failure, 500)]
    [InlineData((ErrorType)99, 500)]
    public void ToHttpStatus_ShouldGiveTheStatusAFailureOfTheTypeIsAnsweredWith(ErrorType type, int status)
        => ErrorTypes.ToHttpStatus(type).ShouldBe(status);

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Unprocessable)]
    [InlineData(ErrorType.Timeout)]
    [InlineData(ErrorType.RateLimit)]
    [InlineData(ErrorType.ExternalFailure)]
    [InlineData(ErrorType.Failure)]
    public void FromHttpStatus_ShouldReadBackTheTypeToHttpStatusAnsweredWith(ErrorType type)
        => ErrorTypes.FromHttpStatus(ErrorTypes.ToHttpStatus(type)).ShouldBe(type);
}
