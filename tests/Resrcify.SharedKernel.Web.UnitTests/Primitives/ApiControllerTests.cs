using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.Web.Primitives;
using Shouldly;
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
        var exception = act.ShouldThrow<InvalidOperationException>();
        exception.Message.ShouldBe("Successful result should not be converted to problem details.");
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
        problemDetails.ShouldNotBeNull();
        problemDetails?.ProblemDetails.Type.ShouldBe(expectedType);
        problemDetails?.ProblemDetails.Title.ShouldBe(expectedTitle);
        problemDetails?.ProblemDetails.Status.ShouldBe(expectedStatusCode);
        problemDetails?.StatusCode.ShouldBe(expectedStatusCode);
        problemDetails?.ProblemDetails.Extensions.ShouldContainKey("errors");
        problemDetails?.ProblemDetails.Extensions["errors"].ShouldBeAssignableTo<IEnumerable<Error>>();
        ((IEnumerable<Error>)problemDetails?.ProblemDetails.Extensions["errors"]!).ShouldContain(error);
    }
}