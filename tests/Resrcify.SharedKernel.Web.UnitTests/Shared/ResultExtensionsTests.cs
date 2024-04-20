using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Resrcify.SharedKernel.ResultFramework.Shared;
using Resrcify.SharedKernel.Web.Shared;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Shared;

public class ResultExtensionsTests
{

    [Fact]
    public async Task Match_WithSuccessResultTask_ShouldReturnSuccess()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success());

        // Act
        var actual = await resultTask.Match(
            onSuccess: () => Results.Ok(),
            onFailure: _ => Results.BadRequest());

        // Assert
        actual
            .Should()
            .BeOfType<Ok>();
    }

    [Fact]
    public async Task Match_WithFailureResultTask_ShouldReturnFailure()
    {
        // Arrange
        var error = new Error("Code", "Message", ErrorType.Validation);
        var resultTask = Task.FromResult(Result.Failure(error));

        // Act
        var actual = await resultTask.Match(
            onSuccess: () => Results.Ok(),
            onFailure: result => Results.BadRequest());

        // Assert
        actual
            .Should()
            .BeOfType<BadRequest>();
    }
    [Fact]
    public void ToProblemDetails_WithSuccessResult_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var result = Result.Success();

        // Act
        // Assert
        result
            .Invoking(r => r.ToProblemDetails())
            .Should()
            .Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(ErrorType.Validation, "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1", "Bad Request", StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.NotFound, "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.4", "Not Found", StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8", "Conflict", StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Failure, "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1", "Internal Server Error", StatusCodes.Status500InternalServerError)]
    public void ToProblemDetails_WithFailureResult_ShouldReturnProblemDetails(ErrorType errorType, string expectedType, string expectedTitle, int expectedStatusCode)
    {
        // Arrange
        var error = new Error("Code", "Message", errorType);
        var result = Result.Failure(error);

        // Act
        var problemDetails = result.ToProblemDetails() as ProblemHttpResult;

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