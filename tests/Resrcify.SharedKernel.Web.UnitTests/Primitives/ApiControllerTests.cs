using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Resrcify.SharedKernel.ResultFramework.Shared;
using Resrcify.SharedKernel.Web.Primitives;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Primitives;
public class ApiControllerTests
{
    [Fact]
    public void ToProblemDetails_WithSuccessResult_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var result = Result.Success();

        // Act & Assert
        var act = () => ApiController.ToProblemDetails(result);

        // Verify that an exception is thrown
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Successful result should not be converted to problem details.");
    }

    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest, "Bad Request", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1")]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound, "Not Found", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.4")]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict, "Conflict", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8")]
    [InlineData(ErrorType.Failure, StatusCodes.Status500InternalServerError, "Internal Server Error", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1")]
    public void ToProblemDetails_WithFailureResult_ShouldReturnProblemDetails(ErrorType errorType, int expectedStatusCode, string expectedTitle, string expectedType)
    {
        // Arrange
        var error = new Error("TestCode", "Test message", errorType);
        var result = Result.Failure(error);

        // Act
        var problemDetails = ApiController.ToProblemDetails(result) as ProblemHttpResult;

        // Assert
        problemDetails.Should().NotBeNull();
        problemDetails?.ProblemDetails.Type.Should().Be(expectedType);
        problemDetails?.ProblemDetails.Title.Should().Be(expectedTitle);
        problemDetails?.ProblemDetails.Status.Should().Be(expectedStatusCode);
        problemDetails?.StatusCode.Should().Be(expectedStatusCode);
        problemDetails?.ProblemDetails.Extensions.Should().ContainKey("Errors");
        problemDetails?.ProblemDetails.Extensions["Errors"].Should().BeAssignableTo<IEnumerable<Error>>();
        ((IEnumerable<Error>)problemDetails?.ProblemDetails.Extensions["Errors"]!).Should().ContainEquivalentOf(error, options => options.ExcludingMissingMembers());
    }
}