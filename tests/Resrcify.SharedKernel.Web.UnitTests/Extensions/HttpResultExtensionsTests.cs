using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class HttpResultExtensionsTests
{
    [Fact]
    public async Task Match_WithSuccessResultTask_ShouldReturnSuccess()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success());

        // Act
        var actual = await resultTask.Match(
            onSuccess: () => Microsoft.AspNetCore.Http.Results.Ok(),
            onFailure: _ => Microsoft.AspNetCore.Http.Results.BadRequest());

        // Assert
        actual
            .ShouldBeOfType<Ok>();
    }

    [Fact]
    public async Task Match_WithFailureResultTask_ShouldReturnFailure()
    {
        // Arrange
        var error = new Error("Code", "Message", ErrorType.Validation);
        var resultTask = Task.FromResult(Result.Failure(error));

        // Act
        var actual = await resultTask.Match(
            onSuccess: () => Microsoft.AspNetCore.Http.Results.Ok(),
            onFailure: result => Microsoft.AspNetCore.Http.Results.BadRequest());

        // Assert
        actual
            .ShouldBeOfType<BadRequest>();
    }


    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest, "Bad Request", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1")]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized, "Unauthorized", "https://datatracker.ietf.org/doc/html/rfc7235#section-3.1")]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden, "Forbidden", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.3")]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound, "Not Found", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.4")]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict, "Conflict", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8")]
    [InlineData(ErrorType.Unprocessable, StatusCodes.Status422UnprocessableEntity, "Unprocessable Content", "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.21")]
    [InlineData(ErrorType.Timeout, StatusCodes.Status504GatewayTimeout, "Gateway Timeout", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.5")]
    [InlineData(ErrorType.RateLimit, StatusCodes.Status429TooManyRequests, "Too Many Requests", "https://datatracker.ietf.org/doc/html/rfc6585#section-4")]
    [InlineData(ErrorType.ExternalFailure, StatusCodes.Status502BadGateway, "Bad Gateway", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.3")]
    [InlineData(ErrorType.Failure, StatusCodes.Status500InternalServerError, "Internal Server Error", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1")]
    public void ToProblemDetails_WithFailureResult_ShouldReturnProblemDetails(
        ErrorType errorType,
        int expectedStatusCode,
        string expectedTitle,
        string expectedType)
    {
        // Arrange
        var error = new Error("TestCode", "Test message", errorType);
        var result = Result.Failure(error);

        // Act
        var problemDetails = result.ToProblemDetails() as ProblemHttpResult;

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

    [Theory]
    [MemberData(nameof(AllErrorCombinations))]
    public void ToProblemDetails_ShouldUseDominantErrorType_WhenMultipleErrors(ErrorType[] inputTypes)
    {
        // Arrange
        var errors = inputTypes
            .Select((t, i) => new Error($"E00{i + 1}", $"Message {i + 1}", t))
            .ToArray();

        var result = Result.Failure(errors);

        // Act
        var httpResult = result.ToProblemDetails() as ProblemHttpResult;

        // Assert
        httpResult.ShouldNotBeNull();

        var problem = httpResult.ProblemDetails;
        problem.ShouldNotBeNull();

        var dominant = typeof(HttpResultExtensions)
            .GetMethod("GetDominantErrorType", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [errors]) as ErrorType? ?? ErrorType.Failure;

        var expectedStatus = HttpResultExtensions.GetStatusCode(dominant);
        var expectedTitle = HttpResultExtensions.GetTitle(dominant);
        var expectedType = HttpResultExtensions.GetType(dominant);

        problem.Status.ShouldBe(expectedStatus);
        problem.Title.ShouldBe(expectedTitle);
        problem.Type.ShouldBe(expectedType);

        problem.Extensions.ShouldContainKey("errors");
        var extensionErrors = problem.Extensions["errors"];
        extensionErrors.ShouldBeAssignableTo<IEnumerable<Error>>();

        var errorList = ((IEnumerable<Error>)extensionErrors!).ToArray();
        errorList.ShouldBe(errors);
    }
    public static IEnumerable<object[]> AllErrorCombinations()
    {
        var types = Enum.GetValues<ErrorType>();

        foreach (var first in types)
        {
            foreach (var second in types)
            {
                if (first != second)
                {
                    yield return new object[] { new[] { first, second } };
                }
            }
        }
    }

    [Fact]
    public void ToProblemDetails_WithSuccessResult_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var result = Result.Success();

        // Act & Assert
        var act = result.ToProblemDetails;

        // Verify that an exception is thrown
        var exception = act.ShouldThrow<InvalidOperationException>();
        exception.Message.ShouldBe("Successful result should not be converted to problem details.");
    }
}
